using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using HarmonyLib;
using UnityEngine;

namespace DuskersCoopMod.Network
{
    public class ConnectedClient
    {
        public int Id;
        public string Name;
        public TcpClient Socket;
        public NetworkStream Stream;
        public StreamReader Reader;
        public StreamWriter Writer;
        public string RemoteInfo;
        public string Version = "Unknown";
        public volatile bool IsActive;

        public void Send(string line)
        {
            try
            {
                if (IsActive && Writer != null)
                {
                    lock (Writer)
                    {
                        Writer.WriteLine(line);
                    }
                }
            }
            catch
            {
                IsActive = false;
            }
        }

        public void Close()
        {
            IsActive = false;
            try { Reader?.Close(); } catch { }
            try { Writer?.Close(); } catch { }
            try { Stream?.Close(); } catch { }
            try { Socket?.Close(); } catch { }
        }
    }

    public class CoopNetworkManager : MonoBehaviour
    {
        public static CoopNetworkManager Instance { get; private set; }

        public static event Action<string> OnHandshakeReceived;
        public static event Action OnSaveSynchronized;

        public NetworkRole Role { get; set; } = NetworkRole.None;
        public bool IsConnected
        {
            get
            {
                if (ConnectedCount > 0) return true;
                if (SteamCoopManager.Instance != null && SteamCoopManager.Instance.IsSteamClient) return true;
                return _isConnected;
            }
        }

        public int ConnectedCount
        {
            get
            {
                int tcpCount = Role == NetworkRole.Host ? _clients.Count : (_isConnected ? 1 : 0);
                int steamCount = (SteamCoopManager.Instance != null) ? SteamCoopManager.Instance.SteamConnectedCount : 0;
                return tcpCount + steamCount;
            }
        }
        public string RemoteEndpointInfo => _remoteInfo;

        public const int DEFAULT_PORT = 7777;
        public const string MOD_VERSION = "1.1.6";

        public string HostVersion => _hostVersion;
        private string _hostVersion = "";

        public int CurrentPort { get; private set; } = DEFAULT_PORT;

        // Multi-client Host data
        private TcpListener _server;
        private readonly List<ConnectedClient> _clients = new List<ConnectedClient>();
        private readonly object _clientsLock = new object();
        private int _clientCounter = 2; // Operator 1 is Host, clients start at 2

        // Client data
        private TcpClient _singleClientSocket;
        private NetworkStream _singleStream;
        private StreamReader _singleReader;
        private StreamWriter _singleWriter;
        private string _remoteInfo = "Disconnected";
        private volatile bool _isConnected;

        // General
        private Thread _mainNetworkThread;
        private volatile bool _isRunning;

        // Thread-safe packet queue for Unity main thread
        private readonly Queue<PacketWrapper> _incomingQueue = new Queue<PacketWrapper>();
        private readonly object _incomingLock = new object();

        // Flag to prevent echoing back
        public bool IsApplyingRemoteConsoleMessage = false;

        // Tactical synchronization timers and state
        private float _lastTacticalDronesSyncTime = 0f;
        private float _lastTacticalDoorsSyncTime = 0f;
        private float _lastTacticalUpgradesSyncTime = 0f;
        private float _lastHostSteeringSyncTime = 0f;
        private float _lastClientDroneSyncTime = 0f;
        private Vector3 _lastReportedClientDronePos = Vector3.zero;
        private float _lastReportedClientDroneRotY = 0f;
        private float _lastClientSteeringTime = 0f;
        private int _lastClientSteeringDrone = -1;
        public float LastClientSteeringTime => _lastClientSteeringTime;
        public int LastClientSteeringDrone => _lastClientSteeringDrone;

        private void Awake()
        {
            Application.runInBackground = true;
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);

                if (CoopPlugin.ConfigPort != null && CoopPlugin.ConfigPort.Value >= 1024 && CoopPlugin.ConfigPort.Value <= 65535)
                {
                    CurrentPort = CoopPlugin.ConfigPort.Value;
                }
            }
            else
            {
                Destroy(gameObject);
            }
        }

        public bool SetPort(int port)
        {
            if (port < 1024 || port > 65535)
            {
                PrintToLocalConsole($"[COOP] Invalid port {port}. Port must be between 1024 and 65535.", ConsoleMessageType.Warning);
                return false;
            }

            CurrentPort = port;
            if (CoopPlugin.ConfigPort != null)
            {
                CoopPlugin.ConfigPort.Value = port;
            }
            PrintToLocalConsole($"[COOP] Server port configured to {port}.", ConsoleMessageType.Info);
            return true;
        }

        // Queue now stores both the packet and (if on Host) the originating client,
        // so we can exclude the sender from echo-broadcasts (e.g. SWAP_UPGRADES).
        private readonly struct IncomingPacket
        {
            public readonly PacketWrapper Packet;
            public readonly ConnectedClient Sender; // null when coming from single-client or Steam
            public IncomingPacket(PacketWrapper p, ConnectedClient s) { Packet = p; Sender = s; }
        }

        private readonly Queue<IncomingPacket> _incomingQueueV2 = new Queue<IncomingPacket>();

        private void Update()
        {
            Application.runInBackground = true;

            // Process incoming packets on Unity's main thread
            List<IncomingPacket> packetsToProcess = null;
            lock (_incomingLock)
            {
                if (_incomingQueue.Count > 0)
                {
                    // Legacy queue (Steam / single-client path)
                    while (_incomingQueue.Count > 0)
                        _incomingQueueV2.Enqueue(new IncomingPacket(_incomingQueue.Dequeue(), null));
                }
                if (_incomingQueueV2.Count > 0)
                {
                    packetsToProcess = new List<IncomingPacket>(_incomingQueueV2);
                    _incomingQueueV2.Clear();
                }
            }

            if (packetsToProcess != null)
            {
                foreach (var ip in packetsToProcess)
                {
                    ProcessPacket(ip.Packet, ip.Sender);
                }
            }

            // Cleanup inactive clients on Host
            if (Role == NetworkRole.Host)
            {
                lock (_clientsLock)
                {
                    for (int i = _clients.Count - 1; i >= 0; i--)
                    {
                        if (!_clients[i].IsActive)
                        {
                            var dead = _clients[i];
                            _clients.RemoveAt(i);
                            PrintToLocalConsole($"[COOP] {dead.Name} disconnected.", ConsoleMessageType.Warning);
                            BroadcastPacket(PacketWrapper.Create("SYSTEM_LOG", "Host", $"[COOP] {dead.Name} disconnected."));
                        }
                    }
                }
            }

            UpdateTacticalSync();
        }

        public void StartHost(int port = 0)
        {
            if (port <= 0) port = CurrentPort;
            else CurrentPort = port;

            Disconnect();
            Role = NetworkRole.Host;
            _isRunning = true;
            _clientCounter = 2;

            _mainNetworkThread = new Thread(() => HostListenerLoop(port))
            {
                IsBackground = true,
                Name = "DuskersCoop_HostListener"
            };
            _mainNetworkThread.Start();

            PrintToLocalConsole($"[COOP] Host started on port {port}. Ready for operators to join.", ConsoleMessageType.SpecialInfo);
        }

        public string LastTargetIp { get; private set; } = "";
        public int LastTargetPort { get; private set; } = DEFAULT_PORT;

        public void ConnectToHost(string ip, int port = DEFAULT_PORT)
        {
            LastTargetIp = ip;
            LastTargetPort = port;

            Disconnect();
            Role = NetworkRole.Client;
            DuskersCoopMod.Save.SaveSlotManager.IsUsingCoopRemoteSlot = true;
            try { GameFileHelper.EnsureGameFileDirectoriesExist(); } catch { }
            _isRunning = true;

            _mainNetworkThread = new Thread(() => ClientWorker(ip, port))
            {
                IsBackground = true,
                Name = "DuskersCoop_ClientThread"
            };
            _mainNetworkThread.Start();

            PrintToLocalConsole($"[COOP] Connecting to Host at {ip}:{port}...", ConsoleMessageType.Info);
        }

        public bool Reconnect()
        {
            if (string.IsNullOrEmpty(LastTargetIp))
            {
                PrintToLocalConsole("[COOP] No previous host saved to reconnect to.", ConsoleMessageType.Warning);
                return false;
            }

            PrintToLocalConsole($"[COOP] Reconnecting to {LastTargetIp}:{LastTargetPort}...", ConsoleMessageType.Info);
            ConnectToHost(LastTargetIp, LastTargetPort);
            return true;
        }

        public void Disconnect()
        {
            _isRunning = false;
            _isConnected = false;
            _remoteInfo = "Disconnected";

            // Disconnect all clients if host
            lock (_clientsLock)
            {
                foreach (var client in _clients)
                {
                    client.Close();
                }
                _clients.Clear();
            }

            try { _server?.Stop(); } catch { }
            _server = null;

            // Disconnect single client
            try { _singleReader?.Close(); } catch { }
            try { _singleWriter?.Close(); } catch { }
            try { _singleStream?.Close(); } catch { }
            try { _singleClientSocket?.Close(); } catch { }

            _singleReader = null;
            _singleWriter = null;
            _singleStream = null;
            _singleClientSocket = null;

            SteamCoopManager.Instance?.LeaveLobby();

            if (DuskersCoopMod.Save.SaveSlotManager.IsUsingCoopRemoteSlot)
            {
                DuskersCoopMod.Save.SaveSlotManager.IsUsingCoopRemoteSlot = false;
                try
                {
                    GameFileHelper.EnsureGameFileDirectoriesExist();
                    GameSaveFile.ReInitSetting();
                    UniverseSaveFile.ReInitSetting();
                }
                catch { }
            }

            if (Role != NetworkRole.None)
            {
                Role = NetworkRole.None;
                PrintToLocalConsole("[COOP] Disconnected from session.", ConsoleMessageType.Warning);
            }
        }

        public List<string> GetConnectedOperatorsList()
        {
            List<string> list = new List<string>();
            if (SteamCoopManager.Instance != null && (SteamCoopManager.Instance.IsSteamHost || SteamCoopManager.Instance.IsSteamClient))
            {
                var sList = SteamCoopManager.Instance.GetSteamOperatorsList();
                if (sList != null) list.AddRange(sList);
            }

            if (Role == NetworkRole.Host)
            {
                if (list.Count == 0)
                {
                    list.Add($"Operator 1 (Host - You) [v{MOD_VERSION}]");
                }
                lock (_clientsLock)
                {
                    foreach (var c in _clients)
                    {
                        if (c.IsActive)
                        {
                            bool mismatch = !string.Equals(c.Version, MOD_VERSION, StringComparison.OrdinalIgnoreCase);
                            string status = mismatch ? $" [v{c.Version} - INCOMPATIBLE!]" : $" [v{c.Version}]";
                            string entry = $" - {c.Name} [{c.RemoteInfo}]{status}";
                            if (!list.Contains(entry))
                            {
                                list.Add(entry);
                            }
                        }
                    }
                }
            }
            else if (Role == NetworkRole.Client && _isConnected)
            {
                if (list.Count == 0)
                {
                    string hVer = !string.IsNullOrEmpty(_hostVersion) ? _hostVersion : "Unknown";
                    bool mismatch = !string.Equals(hVer, MOD_VERSION, StringComparison.OrdinalIgnoreCase);
                    string note = mismatch ? " [INCOMPATIBLE VERSION!]" : "";
                    list.Add($"Connected to Host [{_remoteInfo}] (Host: v{hVer}, You: v{MOD_VERSION}){note}");
                }
            }
            return list;
        }

        public void BroadcastPacket(string rawJson, ConnectedClient exclude = null)
        {
            if (SteamCoopManager.Instance != null && SteamCoopManager.Instance.IsSteamHost)
            {
                SteamCoopManager.Instance.BroadcastP2P(rawJson);
            }

            if (Role != NetworkRole.Host) return;

            lock (_clientsLock)
            {
                foreach (var client in _clients)
                {
                    if (client != exclude && client.IsActive)
                    {
                        client.Send(rawJson);
                    }
                }
            }
        }

        public void SendPacketToHost(string rawJson)
        {
            if (SteamCoopManager.Instance != null && SteamCoopManager.Instance.IsSteamClient)
            {
                SteamCoopManager.Instance.SendP2PToHost(rawJson);
                return;
            }

            if (Role == NetworkRole.Client && _isConnected && _singleWriter != null)
            {
                try
                {
                    lock (_singleWriter)
                    {
                        _singleWriter.WriteLine(rawJson);
                    }
                }
                catch
                {
                    _isConnected = false;
                }
            }
        }

        public void SendCommand(string command)
        {
            string json = PacketWrapper.Create("COMMAND", "Client", new CommandData { command = command });
            SendPacketToHost(json);
        }

        public void SendConsoleText(string text, ConsoleMessageType type, ConsoleMessageFormat format)
        {
            if (Role != NetworkRole.Host) return;

            string json = PacketWrapper.Create("CONSOLE_TEXT", "Host", new ConsoleTextData
            {
                text = text,
                messageType = (int)type,
                messageFormat = (int)format
            });
            BroadcastPacket(json);
        }

        public void BroadcastDoorState(string label, bool isOpen)
        {
            if (Role == NetworkRole.Host && ConnectedCount > 0)
            {
                var packet = new SingleDoorSyncPacket { label = label, isOpen = isOpen };
                BroadcastPacket(PacketWrapper.Create("SINGLE_DOOR_SYNC", "Host", packet));
            }
        }

        private void HostListenerLoop(int port)
        {
            try
            {
                _server = new TcpListener(IPAddress.Any, port);
                _server.Start();

                while (_isRunning)
                {
                    if (!_server.Pending())
                    {
                        Thread.Sleep(50);
                        continue;
                    }

                    TcpClient tcp = _server.AcceptTcpClient();
                    tcp.NoDelay = true;
                    NetworkStream stream = tcp.GetStream();
                    StreamReader reader = new StreamReader(stream, Encoding.UTF8);
                    StreamWriter writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

                    ConnectedClient client;
                    lock (_clientsLock)
                    {
                        int id = _clientCounter++;
                        client = new ConnectedClient
                        {
                            Id = id,
                            Name = $"Operator {id}",
                            Socket = tcp,
                            Stream = stream,
                            Reader = reader,
                            Writer = writer,
                            RemoteInfo = tcp.Client.RemoteEndPoint.ToString(),
                            IsActive = true
                        };
                        _clients.Add(client);
                    }

                    lock (_incomingLock)
                    {
                        _incomingQueueV2.Enqueue(new IncomingPacket(new PacketWrapper
                        {
                            type = "SYSTEM_LOG",
                            data = $"[COOP] {client.Name} joined from {client.RemoteInfo}! (Total: {_clients.Count + 1} operators)"
                        }, null));
                    }

                    // Send Handshake with assigned operator name
                    string handshake = PacketWrapper.Create("HANDSHAKE", "Host", new HandshakeData
                    {
                        playerName = client.Name,
                        version = MOD_VERSION
                    });
                    client.Send(handshake);

                    string saveBase64 = DuskersCoopMod.Save.CoopSaveSyncManager.PackageActiveSave(DuskersCoopMod.Save.SaveSlotManager.CurrentSlot);
                    if (!string.IsNullOrEmpty(saveBase64))
                    {
                        client.Send(PacketWrapper.Create("SAVE_SYNC", "Host", new SaveSyncData
                        {
                            compressedBase64 = saveBase64
                        }));
                    }

                    if (GalaxyMapManager.Instance != null || DungeonManager.Instance != null)
                    {
                        client.Send(PacketWrapper.Create("STRATEGIC_ACTION", "Host", new StrategicActionData
                        {
                            action = "LAUNCH_GAME"
                        }));
                    }

                    DuskersCoopMod.Save.CoopGalaxySyncManager.BroadcastGalaxyState();

                    // Broadcast to other operators
                    BroadcastPacket(PacketWrapper.Create("SYSTEM_LOG", "Host", $"[COOP] {client.Name} joined the command bridge!"), client);

                    // Spawn dedicated reader thread for this client
                    Thread clientThread = new Thread(() => HostClientReader(client))
                    {
                        IsBackground = true,
                        Name = $"DuskersCoop_Client_{client.Id}"
                    };
                    clientThread.Start();
                }
            }
            catch (Exception ex)
            {
                if (_isRunning)
                {
                    Debug.LogWarning($"[DuskersCoopMod] Host listener stopped: {ex.Message}");
                }
            }
        }

        private void HostClientReader(ConnectedClient client)
        {
            try
            {
                while (_isRunning && client.IsActive && client.Socket != null && client.Socket.Connected)
                {
                    string line = client.Reader.ReadLine();
                    if (line == null) break;

                    PacketWrapper packet = PacketWrapper.FromJson(line);
                    if (packet != null)
                    {
                        if (packet.type == "CLIENT_HELLO")
                        {
                            var helloData = packet.GetData<HandshakeData>();
                            string cVer = (helloData != null && !string.IsNullOrEmpty(helloData.version)) ? helloData.version : "Unknown";
                            client.Version = cVer;
                            if (!string.Equals(cVer, MOD_VERSION, StringComparison.OrdinalIgnoreCase))
                            {
                                PrintToLocalConsole($"[COOP CRITICAL] VERSION MISMATCH! You are Host (v{MOD_VERSION}), but {client.Name} connected with v{cVer}!", ConsoleMessageType.Warning);
                                try
                                {
                                    DialogUI.Instance?.ShowDialog(
                                        "COOP VERSION MISMATCH",
                                        $"Operator '{client.Name}' joined with mod version v{cVer}, but you are running v{MOD_VERSION}!\n\nBoth operators must update to the same version.",
                                        ModalWindowType.OK,
                                        null
                                    );
                                }
                                catch { }

                                string mismatchPacket = PacketWrapper.Create("VERSION_MISMATCH", "Host", new HandshakeData
                                {
                                    playerName = "Host",
                                    version = MOD_VERSION
                                });
                                client.Send(mismatchPacket);
                            }
                        }

                        packet.sender = client.Name; // Ensure sender is this client's name

                        // Enqueue with the originating client so ProcessPacket can exclude them from broadcasts
                        lock (_incomingLock)
                        {
                            _incomingQueueV2.Enqueue(new IncomingPacket(packet, client));
                        }

                        // If it's a command, broadcast echo to all OTHER clients so everyone sees it!
                        if (packet.type == "COMMAND")
                        {
                            var cmdData = packet.GetData<CommandData>();
                            if (cmdData != null)
                            {
                                string echo = PacketWrapper.Create("CONSOLE_TEXT", client.Name, new ConsoleTextData
                                {
                                    text = $"[{client.Name}] > {cmdData.command}",
                                    messageType = (int)ConsoleMessageType.Info,
                                    messageFormat = (int)ConsoleMessageFormat.Normal
                                });
                                BroadcastPacket(echo, client);
                            }
                        }
                    }
                }
            }
            catch
            {
            }
            finally
            {
                client.IsActive = false;
            }
        }

        private void ClientWorker(string ip, int port)
        {
            try
            {
                _singleClientSocket = new TcpClient();
                _singleClientSocket.Connect(ip, port);
                _singleClientSocket.NoDelay = true;
                _singleStream = _singleClientSocket.GetStream();
                _singleReader = new StreamReader(_singleStream, Encoding.UTF8);
                _singleWriter = new StreamWriter(_singleStream, Encoding.UTF8) { AutoFlush = true };

                _isConnected = true;
                _remoteInfo = $"{ip}:{port}";

                string hello = PacketWrapper.Create("CLIENT_HELLO", "Client", new HandshakeData
                {
                    playerName = "Operator",
                    version = MOD_VERSION
                });
                _singleWriter.WriteLine(hello);

                lock (_incomingLock)
                {
                    _incomingQueueV2.Enqueue(new IncomingPacket(new PacketWrapper
                    {
                        type = "SYSTEM_LOG",
                        data = $"[COOP] Connected to Host at {_remoteInfo}! Shared terminal bridge active."
                    }, null));
                }

                while (_isRunning && _isConnected && _singleClientSocket != null && _singleClientSocket.Connected)
                {
                    string line = _singleReader.ReadLine();
                    if (line == null) break;

                    PacketWrapper packet = PacketWrapper.FromJson(line);
                    if (packet != null)
                    {
                        EnqueueIncoming(packet);
                    }
                }
            }
            catch (Exception ex)
            {
                lock (_incomingLock)
                {
                    _incomingQueueV2.Enqueue(new IncomingPacket(new PacketWrapper
                    {
                        type = "SYSTEM_LOG",
                        data = $"[COOP] Connection failed: {ex.Message}"
                    }, null));
                }
            }
            finally
            {
                _isConnected = false;
            }
        }

        public void EnqueueIncoming(PacketWrapper packet)
        {
            lock (_incomingLock)
            {
                _incomingQueue.Enqueue(packet);
            }
        }

        // Overload used by Steam path so it still works without sender info
        public void EnqueueIncomingWithSender(PacketWrapper packet, ConnectedClient sender)
        {
            lock (_incomingLock)
            {
                _incomingQueueV2.Enqueue(new IncomingPacket(packet, sender));
            }
        }

        // sender is the ConnectedClient who originated this packet (null for Host-local or Steam).
        // Used to avoid echoing swap/tow actions back to the originator.
        private void ProcessPacket(PacketWrapper packet, ConnectedClient sender = null)
        {
            if (packet == null) return;

            switch (packet.type)
            {
                case "SYSTEM_LOG":
                    PrintToLocalConsole(packet.data, ConsoleMessageType.SpecialInfo);
                    break;

                case "COMMAND":
                    if (Role == NetworkRole.Host)
                    {
                        var cmdData = packet.GetData<CommandData>();
                        if (cmdData != null && !string.IsNullOrEmpty(cmdData.command))
                        {
                            string tag = string.IsNullOrEmpty(packet.sender) ? "Operator" : packet.sender;
                            PrintToLocalConsole($"[{tag}] > {cmdData.command}", ConsoleMessageType.Info);
                            ExecuteAuthoritativeCommand(cmdData.command);
                            BroadcastPacket(PacketWrapper.Create("EXECUTE_COMMAND", tag, cmdData));
                        }
                    }
                    break;

                case "EXECUTE_COMMAND":
                    if (Role == NetworkRole.Client)
                    {
                        var cmdData = packet.GetData<CommandData>();
                        if (cmdData != null && !string.IsNullOrEmpty(cmdData.command))
                        {
                            ExecuteLocalCommand(cmdData.command);
                        }
                    }
                    break;

                case "DRONES_SYNC":
                    if (Role == NetworkRole.Client)
                    {
                        var dronesData = packet.GetData<DronesSyncPacket>();
                        if (dronesData != null)
                        {
                            ApplyDronesSync(dronesData);
                        }
                    }
                    break;

                case "SINGLE_DRONE_SYNC":
                    if (Role == NetworkRole.Client)
                    {
                        var singleDrone = packet.GetData<SingleDroneSyncPacket>();
                        if (singleDrone != null)
                        {
                            ApplySingleDroneSync(singleDrone);
                        }
                    }
                    break;

                case "CLIENT_DRONE_SYNC":
                    if (Role == NetworkRole.Host)
                    {
                        var clientDrone = packet.GetData<ClientDroneSyncPacket>();
                        if (clientDrone != null)
                        {
                            ApplyClientDroneSync(clientDrone);
                        }
                    }
                    break;

                case "DOORS_SYNC":
                    if (Role == NetworkRole.Client)
                    {
                        var doorsData = packet.GetData<DoorsSyncPacket>();
                        if (doorsData != null)
                        {
                            ApplyDoorsSync(doorsData);
                        }
                    }
                    break;

                case "SINGLE_DOOR_SYNC":
                    if (Role == NetworkRole.Client)
                    {
                        var singleDoor = packet.GetData<SingleDoorSyncPacket>();
                        if (singleDoor != null && !string.IsNullOrEmpty(singleDoor.label))
                        {
                            ApplySingleDoorSync(singleDoor.label, singleDoor.isOpen);
                        }
                    }
                    break;

                case "CLIENT_SWAP_UPGRADES":
                    if (Role == NetworkRole.Host)
                    {
                        var swapData = packet.GetData<SwapUpgradesPacket>();
                        if (swapData != null)
                        {
                            ApplySwapUpgrades(swapData);
                            // Bug 5 fix: exclude the originating client so they don't apply the swap twice
                            BroadcastPacket(PacketWrapper.Create("SWAP_UPGRADES", "Host", swapData), exclude: sender);
                        }
                    }
                    break;

                case "SWAP_UPGRADES":
                    if (Role == NetworkRole.Client)
                    {
                        var swapData = packet.GetData<SwapUpgradesPacket>();
                        if (swapData != null)
                        {
                            ApplySwapUpgrades(swapData);
                        }
                    }
                    break;

                case "DUNGEON_SEED":
                    // Bug 1 fix: apply the authoritative dungeon seed so the client generates
                    // exactly the same layout as the Host.
                    if (Role == NetworkRole.Client)
                    {
                        var seedData = packet.GetData<DungeonSeedPacket>();
                        if (seedData != null && seedData.seed != 0)
                        {
                            Patches.DungeonPatches.SynchronizedDungeonSeed = seedData.seed;
                            UnityEngine.Random.seed = seedData.seed;
                            Debug.Log($"[DuskersCoopMod] DUNGEON_SEED received: {seedData.seed} (group: {seedData.dungeonGroup})");
                        }
                    }
                    break;

                case "DRONE_TOW_SYNC":
                    // Bug 4 fix: replicate tow state on the client
                    if (Role == NetworkRole.Client)
                    {
                        var towData = packet.GetData<DroneTowSyncPacket>();
                        if (towData != null)
                        {
                            ApplyDroneTowSync(towData);
                        }
                    }
                    break;

                case "CLIENT_DRONE_TOW":
                    // Bug 4 fix: client requested a tow action — apply on Host and broadcast
                    if (Role == NetworkRole.Host)
                    {
                        var towData = packet.GetData<DroneTowSyncPacket>();
                        if (towData != null)
                        {
                            ApplyDroneTowSync(towData);
                            BroadcastPacket(PacketWrapper.Create("DRONE_TOW_SYNC", "Host", towData), exclude: sender);
                        }
                    }
                    break;

                case "DRONE_UPGRADES_SYNC":
                    if (Role == NetworkRole.Client)
                    {
                        var upgradesData = packet.GetData<DroneUpgradesSyncPacket>();
                        if (upgradesData != null)
                        {
                            ApplyDroneUpgradesSync(upgradesData);
                        }
                    }
                    break;

                case "CONSOLE_TEXT":
                    if (Role == NetworkRole.Client)
                    {
                        var textData = packet.GetData<ConsoleTextData>();
                        if (textData != null && !string.IsNullOrEmpty(textData.text))
                        {
                            IsApplyingRemoteConsoleMessage = true;
                            try
                            {
                                ConsoleMessageType cType = (ConsoleMessageType)textData.messageType;
                                ConsoleMessageFormat cFormat = (ConsoleMessageFormat)textData.messageFormat;
                                PrintToLocalConsole(textData.text, cType, cFormat);
                            }
                            finally
                            {
                                IsApplyingRemoteConsoleMessage = false;
                            }
                        }
                    }
                    break;

                case "HANDSHAKE":
                    var hs = packet.GetData<HandshakeData>();
                    if (hs != null)
                    {
                        _hostVersion = hs.version;
                        PrintToLocalConsole($"[COOP] Handshake verified: Welcome {hs.playerName}! (Host: v{hs.version}, Local: v{MOD_VERSION})", ConsoleMessageType.Benefit);
                        if (!string.IsNullOrEmpty(hs.version) && !string.Equals(hs.version, MOD_VERSION, StringComparison.OrdinalIgnoreCase))
                        {
                            PrintToLocalConsole($"[COOP CRITICAL] VERSION MISMATCH! Host is running v{hs.version}, but you have v{MOD_VERSION} installed! Incompatibilities will occur!", ConsoleMessageType.Warning);
                            try
                            {
                                DialogUI.Instance?.ShowDialog(
                                    "MOD VERSION INCOMPATIBLE",
                                    $"The Host is running Duskers Coop Mod v{hs.version}, but your game has v{MOD_VERSION} installed.\n\nGameplay bugs, missing actions and desyncs will happen! Please update your mod to v{hs.version} to match the Host.",
                                    ModalWindowType.OK,
                                    null
                                );
                            }
                            catch { }
                        }
                        OnHandshakeReceived?.Invoke(hs.playerName);
                    }
                    break;

                case "VERSION_MISMATCH":
                    var vm = packet.GetData<HandshakeData>();
                    string hostV = (vm != null && !string.IsNullOrEmpty(vm.version)) ? vm.version : "Unknown";
                    _hostVersion = hostV;
                    PrintToLocalConsole($"[COOP CRITICAL] VERSION MISMATCH! Host is running v{hostV}, but you have v{MOD_VERSION} installed!", ConsoleMessageType.Warning);
                    try
                    {
                        DialogUI.Instance?.ShowDialog(
                            "MOD VERSION INCOMPATIBLE",
                            $"The Host is running Duskers Coop Mod v{hostV}, but your game has v{MOD_VERSION} installed.\n\nPlease update your mod to v{hostV} to match the Host.",
                            ModalWindowType.OK,
                            null
                        );
                    }
                    catch { }
                    break;

                case "SAVE_SYNC":
                    if (Role == NetworkRole.Client)
                    {
                        var syncData = packet.GetData<SaveSyncData>();
                        if (syncData != null && !string.IsNullOrEmpty(syncData.compressedBase64))
                        {
                            bool ok = DuskersCoopMod.Save.CoopSaveSyncManager.ApplyReceivedSave(syncData.compressedBase64);
                            if (ok)
                            {
                                PrintToLocalConsole("[COOP] Host save synchronized! Shared galaxy and fleet loaded.", ConsoleMessageType.Benefit);
                                OnSaveSynchronized?.Invoke();
                            }
                        }
                    }
                    break;

                case "STRATEGIC_ACTION":
                    if (Role == NetworkRole.Client)
                    {
                        var actData = packet.GetData<StrategicActionData>();
                        if (actData != null && !string.IsNullOrEmpty(actData.action))
                        {
                            HandleStrategicAction(actData);
                        }
                    }
                    break;

                case "GALAXY_STATE":
                    if (Role == NetworkRole.Client)
                    {
                        var stateData = packet.GetData<GalaxyStateData>();
                        if (stateData != null)
                        {
                            DuskersCoopMod.Save.CoopGalaxySyncManager.ApplyGalaxyState(stateData);
                        }
                    }
                    break;
            }
        }

        private void HandleStrategicAction(StrategicActionData data)
        {
            if (data == null || string.IsNullOrEmpty(data.action)) return;

            switch (data.action)
            {
                case "LAUNCH_GAME":
                    try
                    {
                        GlobalSettings.IsTutorial = false;
                        GlobalSettings.FirstTimeIn = true;
                        GalaxyMapManager.PreserveData = true;
                        GalaxyProcessor.universeMapManager = null;

                        try
                        {
                            if (MenuPanelUI.Instance != null)
                            {
                                MenuPanelUI.Instance.Clear();
                                MenuPanelUI.Instance.Reset();
                            }
                        }
                        catch { }

                        UnityEngine.Resources.UnloadUnusedAssets();
                        UnityEngine.Application.LoadLevel("UniverseSceneProcessor");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[DuskersCoopMod] Error auto-launching game on client: {ex}");
                        try
                        {
                            UnityEngine.Application.LoadLevel("UniverseSceneProcessor");
                        }
                        catch {}
                    }
                    break;

                case "TRAVEL_DUNGEON":
                    if (GalaxyMapManager.Instance != null)
                    {
                        try
                        {
                            Patches.GalaxyMapPatches.IsApplyingRemoteAction = true;
                            var tr = HarmonyLib.Traverse.Create(GalaxyMapManager.Instance);
                            StarSystemInfo selSys = tr.Property("SelectedStarSystem").GetValue<StarSystemInfo>() 
                                ?? tr.Field("_selectedStarSystem").GetValue<StarSystemInfo>();
                            if (selSys != null && selSys.Dungeons != null)
                            {
                                var target = selSys.Dungeons.Find(d => 
                                    (!string.IsNullOrEmpty(d.DisplayName) && d.DisplayName.Equals(data.targetName, StringComparison.OrdinalIgnoreCase)) ||
                                    (!string.IsNullOrEmpty(d.Name) && d.Name.Equals(data.targetName, StringComparison.OrdinalIgnoreCase)) ||
                                    d.Id.ToString() == data.targetName ||
                                    d.InternalId.ToString() == data.targetName);
                                if (target != null)
                                {
                                    DuskersCoopMod.Save.CoopGalaxySyncManager.SetShipDungeon(GalaxyMapManager.Instance, target, false);
                                    tr.Method("UpdateGUIVariables")?.GetValue();
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"[DuskersCoopMod] Error handling remote TRAVEL_DUNGEON: {ex}");
                        }
                        finally
                        {
                            Patches.GalaxyMapPatches.IsApplyingRemoteAction = false;
                        }
                    }
                    break;

                case "TRAVEL_SYSTEM":
                    if (GalaxyMapManager.Instance != null)
                    {
                        try
                        {
                            Patches.GalaxyMapPatches.IsApplyingRemoteAction = true;
                            var tr = HarmonyLib.Traverse.Create(GalaxyMapManager.Instance);
                            var nodes = tr.Field("_starSystemNodes").GetValue<System.Collections.IList>();
                            if (nodes != null)
                            {
                                foreach (object node in nodes)
                                {
                                    var info = HarmonyLib.Traverse.Create(node).Property("Info").GetValue<StarSystemInfo>();
                                    if (info != null && (string.Equals(info.Name, data.targetName, StringComparison.OrdinalIgnoreCase) || info.Id.ToString() == data.targetName))
                                    {
                                        DuskersCoopMod.Save.CoopGalaxySyncManager.SetShipStarSystem(GalaxyMapManager.Instance, info, false);
                                        tr.Method("UpdateGUIVariables")?.GetValue();
                                        break;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"[DuskersCoopMod] Error handling remote TRAVEL_SYSTEM: {ex}");
                        }
                        finally
                        {
                            Patches.GalaxyMapPatches.IsApplyingRemoteAction = false;
                        }
                    }
                    break;

                case "SHOW_SYSTEM_VIEW":
                    if (GalaxyMapManager.Instance != null)
                    {
                        try
                        {
                            Patches.GalaxyMapPatches.IsApplyingRemoteAction = true;
                            var tr = HarmonyLib.Traverse.Create(GalaxyMapManager.Instance);
                            var nodes = tr.Field("_starSystemNodes").GetValue<System.Collections.IList>();
                            if (nodes != null)
                            {
                                foreach (object node in nodes)
                                {
                                    var info = HarmonyLib.Traverse.Create(node).Property("Info").GetValue<StarSystemInfo>();
                                    if (info != null && (string.Equals(info.Name, data.targetName, StringComparison.OrdinalIgnoreCase) || info.Id.ToString() == data.targetName))
                                    {
                                        tr.Method("ShowStarSystemView", new object[] { info, false, true })?.GetValue();
                                        break;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"[DuskersCoopMod] Error handling remote SHOW_SYSTEM_VIEW: {ex}");
                        }
                        finally
                        {
                            Patches.GalaxyMapPatches.IsApplyingRemoteAction = false;
                        }
                    }
                    break;

                case "HIDE_SYSTEM_VIEW":
                    if (GalaxyMapManager.Instance != null)
                    {
                        try
                        {
                            Patches.GalaxyMapPatches.IsApplyingRemoteAction = true;
                            HarmonyLib.Traverse.Create(GalaxyMapManager.Instance).Method("HideStarSystemView")?.GetValue();
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"[DuskersCoopMod] Error handling remote HIDE_SYSTEM_VIEW: {ex}");
                        }
                        finally
                        {
                            Patches.GalaxyMapPatches.IsApplyingRemoteAction = false;
                        }
                    }
                    break;

                case "JUMP":
                    if (GalaxyMapManager.Instance != null)
                    {
                        try
                        {
                            Patches.GalaxyMapPatches.IsApplyingRemoteAction = true;
                            HarmonyLib.Traverse.Create(GalaxyMapManager.Instance).Method("ConfirmJump", new object[] { ModalWindowResult.Yes, data.targetName })?.GetValue();
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"[DuskersCoopMod] Error handling remote JUMP: {ex}");
                        }
                        finally
                        {
                            Patches.GalaxyMapPatches.IsApplyingRemoteAction = false;
                        }
                    }
                    break;

                case "SET_MAP_STATE":
                    if (GalaxyMapManager.Instance != null && int.TryParse(data.targetName, out int targetMapState))
                    {
                        try
                        {
                            Patches.GalaxyMapPatches.IsApplyingRemoteAction = true;
                            HarmonyLib.Traverse.Create(GalaxyMapManager.Instance).Method("SetMapState", new object[] { (GalaxyMapState)targetMapState, true, true })?.GetValue();
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"[DuskersCoopMod] Error handling remote SET_MAP_STATE: {ex}");
                        }
                        finally
                        {
                            Patches.GalaxyMapPatches.IsApplyingRemoteAction = false;
                        }
                    }
                    break;

                case "BOARD_DUNGEON":
                    try
                    {
                        Patches.GalaxyMapPatches.IsApplyingRemoteAction = true;
                        GlobalSettings.GameStartedFromGalaxyMap = true;
                        GalaxyMapManager.hasBoardedDungeon = true;

                        string rawTarget = data.targetName ?? "";
                        Debug.Log($"[DuskersCoopMod] [v1.1.2] BOARD_DUNGEON received from host: '{rawTarget}' (Seed: {data.dungeonSeed}, Group: {data.dungeonGroup})");
                        string targetName = rawTarget;
                        string targetGroup = !string.IsNullOrEmpty(data.dungeonGroup) ? data.dungeonGroup : "";
                        if (rawTarget.Contains("|"))
                        {
                            var parts = rawTarget.Split('|');
                            targetName = parts[0];
                            if (string.IsNullOrEmpty(targetGroup) && parts.Length > 1) targetGroup = parts[1];
                        }

                        if (data.dungeonSeed != 0)
                        {
                            Patches.DungeonPatches.SynchronizedDungeonSeed = data.dungeonSeed;
                            UnityEngine.Random.seed = data.dungeonSeed;
                            if (!string.IsNullOrEmpty(targetGroup))
                            {
                                GalaxySaveFile.Save(targetGroup, "SEED_D", data.dungeonSeed);
                            }
                        }

                        DungeonInfo targetDungeon = null;
                        var player = GlobalSettings.GameState != null ? GlobalSettings.GameState.ThePlayer : null;

                        // Search across all star systems to find matching derelict by GroupKey or Name
                        if (GlobalSettings.GameState?.StarSystems != null)
                        {
                            foreach (var sys in GlobalSettings.GameState.StarSystems)
                            {
                                if (sys == null || sys.Dungeons == null) continue;
                                if (!string.IsNullOrEmpty(targetGroup))
                                {
                                    targetDungeon = sys.Dungeons.Find(d => d != null && string.Equals(d.GroupKey, targetGroup, StringComparison.OrdinalIgnoreCase));
                                    if (targetDungeon != null) break;
                                }
                                if (!string.IsNullOrEmpty(targetName))
                                {
                                    targetDungeon = sys.Dungeons.Find(d => d != null && (
                                        (!string.IsNullOrEmpty(d.DisplayName) && string.Equals(d.DisplayName, targetName, StringComparison.OrdinalIgnoreCase)) ||
                                        (!string.IsNullOrEmpty(d.Name) && string.Equals(d.Name, targetName, StringComparison.OrdinalIgnoreCase)) ||
                                        d.Id.ToString() == targetName ||
                                        d.InternalId.ToString() == targetName
                                    ));
                                    if (targetDungeon != null) break;
                                }
                            }
                        }

                        if (targetDungeon == null && player != null)
                        {
                            targetDungeon = player.CurrentDockedDungeon;
                            if (targetDungeon == null && player.CurrentStarSystem?.Dungeons?.Count > 0)
                            {
                                targetDungeon = player.CurrentStarSystem.Dungeons[0];
                            }
                        }

                        if (targetDungeon != null && player != null)
                        {
                            if (targetDungeon.Parent != null)
                            {
                                player.CurrentStarSystem = targetDungeon.Parent;
                            }
                            player.CurrentDockedDungeon = targetDungeon;
                            if (data.dungeonSeed != 0 && !string.IsNullOrEmpty(targetDungeon.GroupKey))
                            {
                                GalaxySaveFile.Save(targetDungeon.GroupKey, "SEED_D", data.dungeonSeed);
                            }
                            if (GalaxyMapManager.Instance != null)
                            {
                                GalaxyMapManager.Instance.SetSelectedDungeon(targetDungeon, false);
                            }
                        }

                        Patches.GalaxyMapPatches.PerformSafeBoarding(GalaxyMapManager.Instance);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[DuskersCoopMod] Error in BOARD_DUNGEON: {ex}. Loading dungeon scene directly.");
                        GlobalSettings.GameStartedFromGalaxyMap = true;
                        GalaxyMapManager.hasBoardedDungeon = true;
                        if (data.dungeonSeed != 0)
                        {
                            Patches.DungeonPatches.SynchronizedDungeonSeed = data.dungeonSeed;
                            UnityEngine.Random.seed = data.dungeonSeed;
                        }
                        if (Mothership.Instance != null) Mothership.Instance.Stop();
                        UnityEngine.Application.LoadLevel("DungeonScene_Generated_Pro");
                    }
                    finally
                    {
                        Patches.GalaxyMapPatches.IsApplyingRemoteAction = false;
                    }
                    break;
            }
        }

        public static bool IsWindowFocused = true;

        private void OnApplicationFocus(bool hasFocus)
        {
            IsWindowFocused = hasFocus;
        }

        public static bool IsSteeringInputActive()
        {
            if (!IsWindowFocused) return false;

            // In Duskers, manual arrow/WASD drone steering ONLY works in Drone View (CameraMode.Drone)
            if (GlobalSettings.cameraMode != CameraMode.Drone) return false;

            // Do not steer if paused or overlay/dialog window is active
            if (GlobalSettings.IsGamePaused || GlobalSettings.ShowingGameOverlayWindow) return false;

            try
            {
                if (DialogUI.Instance != null && DialogUI.Instance.IsShowing) return false;
            }
            catch { }

            try
            {
                if (DroneSwapUi2.Instance != null && DroneSwapUi2.Instance.IsVisible) return false;
            }
            catch { }

            try
            {
                if (Input.GetButton("Up") || Input.GetButton("Down") || Input.GetButton("Left") || Input.GetButton("Right"))
                    return true;
            }
            catch { }

            return Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ||
                   Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ||
                   Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ||
                   Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow);
        }

        private void UpdateTacticalSync()
        {
            if (!IsConnected) return;

            // Host authoritative tactical broadcast
            if (Role == NetworkRole.Host && ConnectedCount > 0)
            {
                // Immediate fast sync for Host's actively steered drone (30 Hz)
                if (DroneManager.Instance != null && DroneManager.Instance.CurrentDrone != null && IsSteeringInputActive())
                {
                    if (Time.time - _lastHostSteeringSyncTime >= 0.033f)
                    {
                        _lastHostSteeringSyncTime = Time.time;
                        var hostDrone = DroneManager.Instance.CurrentDrone;
                        Vector3 hostPos = hostDrone.GetDronePosition();
                        Quaternion hostRot = hostDrone.GetDroneRotation();
                        float hostRotZ = hostRot.eulerAngles.z;

                        BroadcastPacket(PacketWrapper.Create("SINGLE_DRONE_SYNC", "Host", new SingleDroneSyncPacket
                        {
                            droneNumber = hostDrone.DroneNumber,
                            x = hostPos.x,
                            y = hostPos.y,
                            z = hostPos.z,
                            rotZ = hostRotZ,
                            rotY = hostRotZ,
                            hp = hostDrone.CurrentHitPoints,
                            isDead = hostDrone.IsDead
                        }));
                    }
                }

                // Periodic full drones fallback sync at 10 Hz
                if (Time.time - _lastTacticalDronesSyncTime >= 0.10f)
                {
                    _lastTacticalDronesSyncTime = Time.time;
                    if (DroneManager.Instance != null && DroneManager.Instance.dronesList != null && DroneManager.Instance.dronesList.Count > 0)
                    {
                        var packet = new DronesSyncPacket();
                        foreach (var d in DroneManager.Instance.dronesList)
                        {
                            if (d == null) continue;
                            Vector3 pos = d.GetDronePosition();
                            Quaternion rot = d.GetDroneRotation();
                            float rotZ = rot.eulerAngles.z;
                            packet.drones.Add(new DroneSyncItem
                            {
                                droneNumber = d.DroneNumber,
                                x = pos.x,
                                y = pos.y,
                                z = pos.z,
                                rotZ = rotZ,
                                rotY = rotZ,
                                hp = d.CurrentHitPoints,
                                isDead = d.IsDead
                            });
                        }
                        if (packet.drones.Count > 0)
                        {
                            BroadcastPacket(PacketWrapper.Create("DRONES_SYNC", "Host", packet));
                        }
                    }
                }

                // Doors sync at 3 Hz
                if (Time.time - _lastTacticalDoorsSyncTime >= 0.33f)
                {
                    _lastTacticalDoorsSyncTime = Time.time;
                    if (DungeonManager.Instance != null && DungeonManager.Instance.doors != null && DungeonManager.Instance.doors.Length > 0)
                    {
                        var packet = new DoorsSyncPacket();
                        foreach (var door in DungeonManager.Instance.doors)
                        {
                            if (door == null) continue;
                            string label = !string.IsNullOrEmpty(door.LabelSimple) ? door.LabelSimple : door.Label;
                            if (string.IsNullOrEmpty(label)) continue;

                            bool isOpen = door.state == DoorState.Open || door.IsTryingToOpen;
                            packet.doors.Add(new DoorSyncItem
                            {
                                label = label,
                                isOpen = isOpen
                            });
                        }
                        if (packet.doors.Count > 0)
                        {
                            BroadcastPacket(PacketWrapper.Create("DOORS_SYNC", "Host", packet));
                        }
                    }
                }

                // Drone upgrades periodic sync at 1 Hz
                if (Time.time - _lastTacticalUpgradesSyncTime >= 1.0f)
                {
                    _lastTacticalUpgradesSyncTime = Time.time;
                    if (DroneManager.Instance != null && DroneManager.Instance.dronesList != null && DroneManager.Instance.dronesList.Count > 0)
                    {
                        var packet = new DroneUpgradesSyncPacket();
                        foreach (var d in DroneManager.Instance.dronesList)
                        {
                            if (d == null) continue;
                            var item = new DroneUpgradesSyncItem { droneNumber = d.DroneNumber };
                            if (d.Upgrades != null)
                            {
                                for (int s = 0; s < d.Upgrades.Count; s++)
                                {
                                    var up = d.Upgrades[s];
                                    if (up != null && up.Definition != null)
                                    {
                                        item.slots.Add(new UpgradeSlotSyncData
                                        {
                                            slotIndex = s,
                                            type = up.Definition.Type.ToString(),
                                            isBroken = up.IsBroken,
                                            breakFactor = up.UpgradeBreakFactor
                                        });
                                    }
                                }
                            }
                            packet.drones.Add(item);
                        }
                        if (packet.drones.Count > 0)
                        {
                            BroadcastPacket(PacketWrapper.Create("DRONE_UPGRADES_SYNC", "Host", packet));
                        }
                    }
                }
            }
            // Client steering sync to Host at 30 Hz (ONLY when client is actively steering in Drone View!)
            else if (Role == NetworkRole.Client)
            {
                if (DroneManager.Instance != null && DroneManager.Instance.CurrentDrone != null)
                {
                    var curDrone = DroneManager.Instance.CurrentDrone;
                    bool isSteering = IsSteeringInputActive();
                    if (isSteering)
                    {
                        _lastClientSteeringTime = Time.time;
                        _lastClientSteeringDrone = curDrone.DroneNumber;

                        if (Time.time - _lastClientDroneSyncTime >= 0.033f)
                        {
                            _lastClientDroneSyncTime = Time.time;
                            Vector3 curPos = curDrone.GetDronePosition();
                            float curRotZ = curDrone.GetDroneRotation().eulerAngles.z;

                            _lastReportedClientDronePos = curPos;
                            _lastReportedClientDroneRotY = curRotZ;
                            SendPacketToHost(PacketWrapper.Create("CLIENT_DRONE_SYNC", "Operator", new ClientDroneSyncPacket
                            {
                                droneNumber = curDrone.DroneNumber,
                                x = curPos.x,
                                y = curPos.y,
                                z = curPos.z,
                                rotZ = curRotZ,
                                rotY = curRotZ
                            }));
                        }
                    }
                }
            }

            // Continuously maintain visual hierarchy and positioning for all drones on this machine
            if (DroneManager.Instance != null && DroneManager.Instance.dronesList != null)
            {
                foreach (var d in DroneManager.Instance.dronesList)
                {
                    if (d != null)
                    {
                        SyncDroneVisualHierarchy(d);
                    }
                }
            }
        }

        public static void SyncDroneVisualHierarchy(Drone drone)
        {
            if (drone == null) return;
            try
            {
                var tr = Traverse.Create(drone);

                var label = tr.Field<GameObject>("_labelSV")?.Value;
                var labelRef = tr.Field<GameObject>("_labelSV_Reference")?.Value;
                if (label != null && labelRef != null)
                {
                    label.transform.position = labelRef.transform.position;
                    label.transform.rotation = labelRef.transform.rotation;
                }

                var img = tr.Field<GameObject>("_imagePlaneSV")?.Value;
                var imgRef = tr.Field<GameObject>("_imagePlaneSV_Reference")?.Value;
                if (img != null && imgRef != null)
                {
                    img.transform.position = imgRef.transform.position;
                    img.transform.rotation = imgRef.transform.rotation;
                }

                var turret = tr.Field<GameObject>("_turretOverlay")?.Value;
                var turretRef = tr.Field<GameObject>("_turretOverlay_Reference")?.Value;
                if (turret != null && turretRef != null)
                {
                    turret.transform.position = turretRef.transform.position;
                    turret.transform.rotation = turretRef.transform.rotation;
                }

                var shield = tr.Field<GameObject>("_shieldOverlay")?.Value;
                var shieldRef = tr.Field<GameObject>("_shieldOverlay_Reference")?.Value;
                if (shield != null && shieldRef != null)
                {
                    shield.transform.position = shieldRef.transform.position;
                    shield.transform.rotation = shieldRef.transform.rotation;
                }

                if (drone.droneUIObject != null)
                {
                    drone.droneUIObject.RefreshInfoLabelPos();
                }

                // If in Drone View, ensure 3D model visibility for other drones in the same room
                if (GlobalSettings.cameraMode == CameraMode.Drone)
                {
                    var curDrone = DroneManager.Instance?.CurrentDrone;
                    if (curDrone != null && drone != curDrone)
                    {
                        bool sameRoom = drone.CurrentRoom != null && curDrone.CurrentRoom != null &&
                                        (drone.CurrentRoom == curDrone.CurrentRoom || drone.CurrentRoom.boardingVessel);
                        if (drone.droneViewModel != null)
                        {
                            drone.droneViewModel.SetActive(sameRoom);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DuskersCoopMod] Error syncing drone visuals for Drone {drone.DroneNumber}: {ex.Message}");
            }
        }

        public void ApplySwapUpgrades(SwapUpgradesPacket packet)
        {
            if (packet == null || DroneManager.Instance == null || DroneManager.Instance.dronesList == null) return;
            try
            {
                Drone droneA = DroneManager.Instance.dronesList.Find(d => d != null && d.DroneNumber == packet.droneA);
                Drone droneB = DroneManager.Instance.dronesList.Find(d => d != null && d.DroneNumber == packet.droneB);
                if (droneA == null || droneB == null) return;

                Patches.DroneSwapPatches.IsApplyingRemoteSwap = true;
                try
                {
                    var swapUI = DroneSwapUi2.Instance;
                    if (swapUI != null && swapUI.IsVisible)
                    {
                        var panels = Traverse.Create(swapUI).Field("_dronePanels").GetValue<DroneSwapDroneInfoPanel[]>();
                        if (panels != null && panels.Length >= 2 && panels[0].Drone == droneA && panels[1].Drone == droneB)
                        {
                            Traverse.Create(swapUI).Method("SwapSpecifiedSlots", packet.slotA, packet.slotB).GetValue();
                            return;
                        }
                    }

                    BaseDroneUpgrade upA = droneA.PullUpgrade(packet.slotA);
                    BaseDroneUpgrade upB = droneB.PullUpgrade(packet.slotB);

                    if (upA != null)
                    {
                        droneB.AddDroneUpgrade(packet.slotB, upA);
                    }
                    if (upB != null)
                    {
                        droneA.AddDroneUpgrade(packet.slotA, upB);
                    }

                    if (swapUI != null && swapUI.IsVisible)
                    {
                        var panels = Traverse.Create(swapUI).Field("_dronePanels").GetValue<DroneSwapDroneInfoPanel[]>();
                        if (panels != null && panels.Length >= 2 && panels[0].Drone != null && panels[1].Drone != null)
                        {
                            swapUI.SetDrones(panels[0].Drone, panels[1].Drone);
                        }
                    }

                    if (SchematicViewCanvas.Instance != null)
                    {
                        SchematicViewCanvas.Instance.RefreshDrone(droneA.DroneNumber);
                        SchematicViewCanvas.Instance.RefreshDrone(droneB.DroneNumber);
                    }

                    if (DroneManager.Instance != null && DroneManager.Instance.currentDronePanel != null)
                    {
                        DroneManager.Instance.currentDronePanel.UpgradesChanged = true;
                    }
                }
                finally
                {
                    Patches.DroneSwapPatches.IsApplyingRemoteSwap = false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DuskersCoopMod] Error applying remote swap: {ex}");
            }
        }

        private void ApplyDroneUpgradesSync(DroneUpgradesSyncPacket packet)
        {
            if (packet == null || packet.drones == null || DroneManager.Instance == null || DroneManager.Instance.dronesList == null) return;

            try
            {
                bool anyChanged = false;
                foreach (var item in packet.drones)
                {
                    if (item == null) continue;
                    var drone = DroneManager.Instance.dronesList.Find(d => d != null && d.DroneNumber == item.droneNumber);
                    if (drone == null) continue;

                    bool droneChanged = false;
                    int maxSlots = drone.NumberOfUpgradeSlots;

                    for (int s = 0; s < maxSlots; s++)
                    {
                        var slotData = item.slots != null ? item.slots.Find(x => x != null && x.slotIndex == s) : null;
                        BaseDroneUpgrade currentUp = (drone.Upgrades != null && s < drone.Upgrades.Count) ? drone.Upgrades[s] : null;

                        if (slotData == null || string.IsNullOrEmpty(slotData.type))
                        {
                            if (currentUp != null)
                            {
                                drone.RemoveDroneUpgrade(s);
                                droneChanged = true;
                            }
                        }
                        else
                        {
                            string curTypeStr = (currentUp != null && currentUp.Definition != null) ? currentUp.Definition.Type.ToString() : null;
                            if (!string.Equals(curTypeStr, slotData.type, StringComparison.OrdinalIgnoreCase))
                            {
                                try
                                {
                                    DroneUpgradeType parsedType = (DroneUpgradeType)Enum.Parse(typeof(DroneUpgradeType), slotData.type, true);
                                    BaseDroneUpgrade newUp = DroneUpgradeFactory.CreateUpgradeInstance(parsedType);
                                    if (newUp != null)
                                    {
                                        if (slotData.isBroken)
                                        {
                                            newUp.Break();
                                        }
                                        drone.AddDroneUpgrade(s, newUp);
                                        droneChanged = true;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.LogWarning($"[DuskersCoopMod] Could not parse upgrade type '{slotData.type}': {ex.Message}");
                                }
                            }
                        }
                    }

                    if (droneChanged)
                    {
                        anyChanged = true;
                        if (SchematicViewCanvas.Instance != null)
                        {
                            SchematicViewCanvas.Instance.RefreshDrone(drone.DroneNumber);
                        }
                    }
                }

                if (anyChanged)
                {
                    if (DroneManager.Instance.currentDronePanel != null)
                    {
                        DroneManager.Instance.currentDronePanel.UpgradesChanged = true;
                    }

                    var swapUI = DroneSwapUi2.Instance;
                    if (swapUI != null && swapUI.IsVisible)
                    {
                        var panels = Traverse.Create(swapUI).Field("_dronePanels").GetValue<DroneSwapDroneInfoPanel[]>();
                        if (panels != null && panels.Length >= 2 && panels[0].Drone != null && panels[1].Drone != null)
                        {
                            swapUI.SetDrones(panels[0].Drone, panels[1].Drone);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DuskersCoopMod] Error in ApplyDroneUpgradesSync: {ex}");
            }
        }

        private void ApplySingleDroneSync(SingleDroneSyncPacket packet)
        {
            if (packet == null || DroneManager.Instance == null || DroneManager.Instance.dronesList == null) return;

            try
            {
                var drone = DroneManager.Instance.dronesList.Find(d => d != null && d.DroneNumber == packet.droneNumber);
                if (drone != null)
                {
                    // Only skip Host's authoritative update if the client is actively steering THIS EXACT drone RIGHT NOW in Drone View!
                    bool clientSteeringThisNow = (DroneManager.Instance.CurrentDrone == drone) && IsSteeringInputActive();
                    if (clientSteeringThisNow) return;

                    Vector3 targetPos = new Vector3(packet.x, packet.y, 0f);
                    drone.MoveToPosition(targetPos);
                    drone.LastPosition = targetPos;
                    Traverse.Create(drone).Field("lastPosition")?.SetValue(targetPos);
                    drone.CurrentRawSpeed = 0f;
                    Traverse.Create(drone).Field("_directionalForce")?.SetValue(Vector3.zero);
                    Traverse.Create(drone).Field("distPerFrame")?.SetValue(Vector3.zero);
                    if (drone.transform != null)
                    {
                        float targetRotZ = (packet.rotZ != 0f) ? packet.rotZ : packet.rotY;
                        drone.transform.rotation = Quaternion.Euler(0f, 0f, targetRotZ);
                        Traverse.Create(drone).Field("_heading")?.SetValue(drone.transform.up);
                    }
                    SyncDroneVisualHierarchy(drone);
                    try
                    {
                        DroneManager.Instance?.CalcDroneCurrentRoom(drone);
                        DroneManager.Instance?.CalcDroneCurrentCorridor(drone);
                    }
                    catch { }

                    if (DroneManager.Instance.CurrentDrone == drone)
                    {
                        _lastReportedClientDronePos = targetPos;
                        _lastReportedClientDroneRotY = (packet.rotZ != 0f) ? packet.rotZ : packet.rotY;
                        if (GlobalSettings.cameraMode == CameraMode.Drone)
                        {
                            DroneManager.Instance.positionDroneCamera();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DuskersCoopMod] Error applying SingleDroneSync for Drone {packet.droneNumber}: {ex.Message}");
            }
        }

        private void ApplyDronesSync(DronesSyncPacket packet)
        {
            if (packet == null || packet.drones == null || DroneManager.Instance == null || DroneManager.Instance.dronesList == null) return;

            foreach (var item in packet.drones)
            {
                if (item == null) continue;
                try
                {
                    var drone = DroneManager.Instance.dronesList.Find(d => d != null && d.DroneNumber == item.droneNumber);
                    if (drone != null)
                    {
                        bool clientSteeringThisNow = (DroneManager.Instance.CurrentDrone == drone) && IsSteeringInputActive();
                        if (clientSteeringThisNow) continue;

                        Vector3 targetPos = new Vector3(item.x, item.y, 0f);
                        drone.MoveToPosition(targetPos);
                        drone.LastPosition = targetPos;
                        Traverse.Create(drone).Field("lastPosition")?.SetValue(targetPos);
                        drone.CurrentRawSpeed = 0f;
                        Traverse.Create(drone).Field("_directionalForce")?.SetValue(Vector3.zero);
                        Traverse.Create(drone).Field("distPerFrame")?.SetValue(Vector3.zero);
                        if (drone.transform != null)
                        {
                            float targetRotZ = (item.rotZ != 0f) ? item.rotZ : item.rotY;
                            drone.transform.rotation = Quaternion.Euler(0f, 0f, targetRotZ);
                            Traverse.Create(drone).Field("_heading")?.SetValue(drone.transform.up);
                        }
                        SyncDroneVisualHierarchy(drone);
                        try
                        {
                            DroneManager.Instance?.CalcDroneCurrentRoom(drone);
                            DroneManager.Instance?.CalcDroneCurrentCorridor(drone);
                        }
                        catch { }

                        if (DroneManager.Instance.CurrentDrone == drone)
                        {
                            _lastReportedClientDronePos = targetPos;
                            _lastReportedClientDroneRotY = (item.rotZ != 0f) ? item.rotZ : item.rotY;
                            if (GlobalSettings.cameraMode == CameraMode.Drone)
                            {
                                DroneManager.Instance.positionDroneCamera();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[DuskersCoopMod] Error applying DronesSync for Drone {item.droneNumber}: {ex.Message}");
                }
            }
        }

        private void ApplyClientDroneSync(ClientDroneSyncPacket packet)
        {
            if (packet == null || DroneManager.Instance == null || DroneManager.Instance.dronesList == null) return;

            try
            {
                var drone = DroneManager.Instance.dronesList.Find(d => d != null && d.DroneNumber == packet.droneNumber);
                if (drone != null)
                {
                    // If Host is NOT actively steering this exact drone right now, accept client's position!
                    bool hostSteeringThisDrone = (DroneManager.Instance.CurrentDrone == drone) && IsSteeringInputActive();
                    if (!hostSteeringThisDrone)
                    {
                        Vector3 targetPos = new Vector3(packet.x, packet.y, 0f);
                        drone.MoveToPosition(targetPos);
                        drone.LastPosition = targetPos;
                        Traverse.Create(drone).Field("lastPosition")?.SetValue(targetPos);
                        drone.CurrentRawSpeed = 0f;
                        Traverse.Create(drone).Field("_directionalForce")?.SetValue(Vector3.zero);
                        Traverse.Create(drone).Field("distPerFrame")?.SetValue(Vector3.zero);
                        if (drone.transform != null)
                        {
                            float targetRotZ = (packet.rotZ != 0f) ? packet.rotZ : packet.rotY;
                            drone.transform.rotation = Quaternion.Euler(0f, 0f, targetRotZ);
                            Traverse.Create(drone).Field("_heading")?.SetValue(drone.transform.up);
                        }
                        SyncDroneVisualHierarchy(drone);
                        try
                        {
                            DroneManager.Instance?.CalcDroneCurrentRoom(drone);
                            DroneManager.Instance?.CalcDroneCurrentCorridor(drone);
                        }
                        catch { }

                        if (DroneManager.Instance.CurrentDrone == drone && GlobalSettings.cameraMode == CameraMode.Drone)
                        {
                            DroneManager.Instance.positionDroneCamera();
                        }

                        // Broadcast to other clients if more than one is connected
                        if (ConnectedCount > 1)
                        {
                            BroadcastPacket(PacketWrapper.Create("SINGLE_DRONE_SYNC", "Host", new SingleDroneSyncPacket
                            {
                                droneNumber = drone.DroneNumber,
                                x = targetPos.x,
                                y = targetPos.y,
                                z = targetPos.z,
                                rotZ = (packet.rotZ != 0f) ? packet.rotZ : packet.rotY,
                                rotY = (packet.rotZ != 0f) ? packet.rotZ : packet.rotY,
                                hp = drone.CurrentHitPoints,
                                isDead = drone.IsDead
                            }));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DuskersCoopMod] Error applying ClientDroneSync for Drone {packet.droneNumber}: {ex.Message}");
            }
        }

        public void ApplySingleDoorSync(string doorLabel, bool isOpen)
        {
            if (string.IsNullOrEmpty(doorLabel) || DungeonManager.Instance == null || DungeonManager.Instance.doors == null) return;

            Patches.DoorPatches.IsApplyingDoorSync = true;
            try
            {
                foreach (var door in DungeonManager.Instance.doors)
                {
                    if (door == null) continue;
                    string label = !string.IsNullOrEmpty(door.LabelSimple) ? door.LabelSimple : door.Label;
                    if (string.Equals(label, doorLabel, StringComparison.OrdinalIgnoreCase))
                    {
                        if (isOpen)
                        {
                            ForceOpenDoor(door);
                        }
                        else
                        {
                            ForceCloseDoor(door);
                        }
                        break;
                    }
                }
            }
            finally
            {
                Patches.DoorPatches.IsApplyingDoorSync = false;
            }
        }

        private void ApplyDoorsSync(DoorsSyncPacket packet)
        {
            if (packet == null || packet.doors == null || DungeonManager.Instance == null || DungeonManager.Instance.doors == null) return;

            Patches.DoorPatches.IsApplyingDoorSync = true;
            try
            {
                foreach (var item in packet.doors)
                {
                    if (item == null || string.IsNullOrEmpty(item.label)) continue;
                    foreach (var door in DungeonManager.Instance.doors)
                    {
                        if (door == null) continue;
                        string label = !string.IsNullOrEmpty(door.LabelSimple) ? door.LabelSimple : door.Label;
                        if (string.Equals(label, item.label, StringComparison.OrdinalIgnoreCase))
                        {
                            if (item.isOpen)
                            {
                                ForceOpenDoor(door);
                            }
                            else
                            {
                                ForceCloseDoor(door);
                            }
                            break;
                        }
                    }
                }
            }
            finally
            {
                Patches.DoorPatches.IsApplyingDoorSync = false;
            }
        }

        private void ForceOpenDoor(Door door)
        {
            if (door == null) return;
            if (door.state != DoorState.Open || door.IsTryingToClose)
            {
                door.state = DoorState.Closed;
                door.open(false, false);
                door.state = DoorState.Open;
                Traverse.Create(door).Property("IsTryingToClose")?.SetValue(false);
            }

            EnsureDoorVisuals(door, true);

            if (DungeonManager.Instance != null && !GlobalSettings.MissionStarted)
            {
                var boardingAirlock = BoardingShip.Instance?.CurrentAirlock?.door;
                if (boardingAirlock == door)
                {
                    try { DungeonManager.Instance.StartMission(false); } catch { }
                }
            }
        }

        private void ForceCloseDoor(Door door)
        {
            if (door == null) return;
            if (door.state != DoorState.Closed || door.IsTryingToOpen)
            {
                door.state = DoorState.Open;
                try
                {
                    Traverse.Create(door).Method("CloseDoor")?.GetValue();
                }
                catch
                {
                    door.close(false);
                }
                door.state = DoorState.Closed;
                Traverse.Create(door).Property("IsTryingToOpen")?.SetValue(false);
            }

            EnsureDoorVisuals(door, false);
        }

        private void EnsureDoorVisuals(Door door, bool isOpen)
        {
            if (door == null) return;
            try
            {
                var fillRenderer = Traverse.Create(door).Field("fillSVCorridorRenderer")?.GetValue<Renderer>();
                if (isOpen)
                {
                    if (door.sliderA != null && door.sliderB != null)
                    {
                        if (door.sliderA.localPosition.y < 0.5f)
                        {
                            door.sliderA.Translate(0f, 1f, 0f);
                            door.sliderB.Translate(0f, -1f, 0f);
                        }
                    }
                    if (fillRenderer != null)
                    {
                        fillRenderer.enabled = true;
                    }
                    if (door.tiles != null && door.tiles.Length > 0 && door.onSchematic)
                    {
                        for (int i = 0; i < door.tiles.Length; i++)
                        {
                            if (door.tiles[i] != null) door.tiles[i].SetActive(true);
                        }
                    }
                    door.AirlockOpenedEvent?.Invoke(door);
                    door.DoorOpenedEvent?.Invoke(door);
                }
                else
                {
                    if (door.sliderA != null && door.sliderB != null)
                    {
                        if (door.sliderA.localPosition.y > 0.5f)
                        {
                            door.sliderA.Translate(0f, -1f, 0f);
                            door.sliderB.Translate(0f, 1f, 0f);
                        }
                    }
                    if (fillRenderer != null)
                    {
                        fillRenderer.enabled = false;
                    }
                    if (door.tiles != null && door.tiles.Length > 0 && door.onSchematic)
                    {
                        for (int i = 0; i < door.tiles.Length; i++)
                        {
                            if (door.tiles[i] != null) door.tiles[i].SetActive(false);
                        }
                    }
                    door.AirlockClosedEvent?.Invoke(door);
                    door.DoorClosedEvent?.Invoke(door);
                }
            }
            catch { }
        }



        private void ExecuteLocalCommand(string command)
        {
            if (string.IsNullOrEmpty(command) || ConsoleWindow3.Instance == null) return;

            // Bug 3 fix: save whatever the player was currently typing so we can restore it
            // after the remote command is injected, preventing the terminal from wiping their input.
            string savedInput = null;
            int savedCursor = 0;
            try
            {
                var tr = Traverse.Create(ConsoleWindow3.Instance);
                savedInput = tr.Field("_commandText").GetValue<string>() ?? "";
                savedCursor = tr.Field("_cursorPosition").GetValue<int>();
            }
            catch { }

            Patches.ConsolePatches.IsExecutingRemoteCommand = true;
            try
            {
                ConsoleWindow3.Instance.InjectCommandText(command);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DuskersCoopMod] Error executing replicated command '{command}': {ex}");
            }
            finally
            {
                Patches.ConsolePatches.IsExecutingRemoteCommand = false;

                // Restore the player's in-progress input
                if (savedInput != null)
                {
                    try
                    {
                        var tr = Traverse.Create(ConsoleWindow3.Instance);
                        tr.Field("_commandText").SetValue(savedInput);
                        int restoredCursor = Math.Min(savedCursor, savedInput.Length);
                        tr.Field("_cursorPosition").SetValue(restoredCursor);
                        tr.Method("RefreshCurrentLine").GetValue();
                    }
                    catch { }
                }
            }
        }

        public void ExecuteAuthoritativeCommand(string command)
        {
            if (ConsoleWindow3.Instance == null) return;

            // Bug 3 fix: same as ExecuteLocalCommand — save and restore player's typed input
            string savedInput = null;
            int savedCursor = 0;
            try
            {
                var tr = Traverse.Create(ConsoleWindow3.Instance);
                savedInput = tr.Field("_commandText").GetValue<string>() ?? "";
                savedCursor = tr.Field("_cursorPosition").GetValue<int>();
            }
            catch { }

            Patches.ConsolePatches.IsExecutingRemoteCommand = true;
            try
            {
                ConsoleWindow3.Instance.InjectCommandText(command);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DuskersCoopMod] Error executing authoritative command '{command}': {ex}");
            }
            finally
            {
                Patches.ConsolePatches.IsExecutingRemoteCommand = false;

                if (savedInput != null)
                {
                    try
                    {
                        var tr = Traverse.Create(ConsoleWindow3.Instance);
                        tr.Field("_commandText").SetValue(savedInput);
                        int restoredCursor = Math.Min(savedCursor, savedInput.Length);
                        tr.Field("_cursorPosition").SetValue(restoredCursor);
                        tr.Method("RefreshCurrentLine").GetValue();
                    }
                    catch { }
                }
            }
        }

        // Bug 4 fix: apply tow/release state received from the network.
        // Looks for the "carry" relationship between drones via Reflection since Duskers
        // does not expose a clean API for this.
        private void ApplyDroneTowSync(DroneTowSyncPacket packet)
        {
            if (packet == null || DroneManager.Instance == null || DroneManager.Instance.dronesList == null) return;
            try
            {
                var tower = DroneManager.Instance.dronesList.Find(d => d != null && d.DroneNumber == packet.towerDroneNumber);
                var towed  = packet.towedDroneNumber >= 0
                    ? DroneManager.Instance.dronesList.Find(d => d != null && d.DroneNumber == packet.towedDroneNumber)
                    : null;

                if (tower == null) return;

                if (packet.isTowing && towed != null)
                {
                    // Try to set the "_towedDrone" / "towedDrone" field on the tower drone
                    var towedField = Traverse.Create(tower).Field("_towedDrone");
                    if (towedField == null || towedField.GetValue<object>() == null)
                        towedField = Traverse.Create(tower).Field("towedDrone");
                    if (towedField != null)
                    {
                        towedField.SetValue(towed);
                        Debug.Log($"[DuskersCoopMod] TowSync: Drone {tower.DroneNumber} now towing Drone {towed.DroneNumber}");
                    }

                    // Also keep the towed drone positioned inside the tower's bounds immediately
                    Vector3 towerPos = tower.GetDronePosition();
                    towed.MoveToPosition(towerPos);
                    towed.LastPosition = towerPos;
                    SyncDroneVisualHierarchy(towed);
                }
                else
                {
                    // Release: clear the reference on tower
                    var towedField = Traverse.Create(tower).Field("_towedDrone");
                    if (towedField == null || towedField.GetValue<object>() == null)
                        towedField = Traverse.Create(tower).Field("towedDrone");
                    if (towedField != null)
                    {
                        towedField.SetValue(null);
                        Debug.Log($"[DuskersCoopMod] TowSync: Drone {tower.DroneNumber} released tow.");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DuskersCoopMod] ApplyDroneTowSync error: {ex.Message}");
            }
        }

        public void PrintToLocalConsole(string message, ConsoleMessageType type = ConsoleMessageType.Info, ConsoleMessageFormat format = ConsoleMessageFormat.Normal)
        {
            if (ConsoleWindow3.Instance != null)
            {
                try
                {
                    Traverse.Create(ConsoleWindow3.Instance)
                        .Method("AddTextToConsole", new object[] { new ConsoleMessage(message, type, format) })
                        .GetValue();
                }
                catch
                {
                    Debug.Log($"[DuskersCoopMod] {message}");
                }
            }
            else
            {
                Debug.Log($"[DuskersCoopMod] {message}");
            }
        }

        private void OnDestroy()
        {
            Disconnect();
        }

        private void OnApplicationQuit()
        {
            Disconnect();
        }
    }
}
