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
                if (SteamCoopManager.Instance != null && (SteamCoopManager.Instance.IsSteamHost || SteamCoopManager.Instance.IsSteamClient))
                {
                    return SteamCoopManager.Instance.SteamConnectedCount > 0 || SteamCoopManager.Instance.IsSteamClient;
                }
                return Role == NetworkRole.Host ? _clients.Count > 0 : _isConnected;
            }
        }

        public int ConnectedCount
        {
            get
            {
                if (SteamCoopManager.Instance != null && (SteamCoopManager.Instance.IsSteamHost || SteamCoopManager.Instance.IsSteamClient))
                {
                    return SteamCoopManager.Instance.SteamConnectedCount;
                }
                return Role == NetworkRole.Host ? _clients.Count : (_isConnected ? 1 : 0);
            }
        }
        public string RemoteEndpointInfo => _remoteInfo;

        public const int DEFAULT_PORT = 7777;
        public const string MOD_VERSION = "1.1.0";

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

        private void Awake()
        {
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

        private void Update()
        {
            // Process incoming packets on Unity's main thread
            List<PacketWrapper> packetsToProcess = null;
            lock (_incomingLock)
            {
                if (_incomingQueue.Count > 0)
                {
                    packetsToProcess = new List<PacketWrapper>(_incomingQueue);
                    _incomingQueue.Clear();
                }
            }

            if (packetsToProcess != null)
            {
                foreach (var packet in packetsToProcess)
                {
                    ProcessPacket(packet);
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
            if (SteamCoopManager.Instance != null && (SteamCoopManager.Instance.IsSteamHost || SteamCoopManager.Instance.IsSteamClient))
            {
                return SteamCoopManager.Instance.GetSteamOperatorsList();
            }

            List<string> list = new List<string>();
            if (Role == NetworkRole.Host)
            {
                list.Add("Operator 1 (Host - You)");
                lock (_clientsLock)
                {
                    foreach (var c in _clients)
                    {
                        if (c.IsActive)
                        {
                            list.Add($"{c.Name} [{c.RemoteInfo}]");
                        }
                    }
                }
            }
            else if (Role == NetworkRole.Client && _isConnected)
            {
                list.Add($"Connected to Host [{_remoteInfo}]");
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

        public void SendCommand(string command)
        {
            if (SteamCoopManager.Instance != null && SteamCoopManager.Instance.IsSteamClient)
            {
                string p2pJson = PacketWrapper.Create("COMMAND", "Client", new CommandData { command = command });
                SteamCoopManager.Instance.SendP2PToHost(p2pJson);
                return;
            }

            if (Role == NetworkRole.Client && _isConnected && _singleWriter != null)
            {
                string json = PacketWrapper.Create("COMMAND", "Client", new CommandData { command = command });
                try
                {
                    lock (_singleWriter)
                    {
                        _singleWriter.WriteLine(json);
                    }
                }
                catch
                {
                    _isConnected = false;
                }
            }
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

                    EnqueueIncoming(new PacketWrapper
                    {
                        type = "SYSTEM_LOG",
                        data = $"[COOP] {client.Name} joined from {client.RemoteInfo}! (Total: {_clients.Count + 1} operators)"
                    });

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
                        packet.sender = client.Name; // Ensure sender is this client's name
                        EnqueueIncoming(packet);

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

                EnqueueIncoming(new PacketWrapper
                {
                    type = "SYSTEM_LOG",
                    data = $"[COOP] Connected to Host at {_remoteInfo}! Shared terminal bridge active."
                });

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
                EnqueueIncoming(new PacketWrapper
                {
                    type = "SYSTEM_LOG",
                    data = $"[COOP] Connection failed: {ex.Message}"
                });
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

        private void ProcessPacket(PacketWrapper packet)
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
                        PrintToLocalConsole($"[COOP] Handshake verified: Welcome {hs.playerName}! (v{hs.version})", ConsoleMessageType.Benefit);
                        OnHandshakeReceived?.Invoke(hs.playerName);
                    }
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

                        if (MainMenu.Instance != null)
                        {
                            try
                            {
                                MainMenu.LaunchGameFinal();
                                return;
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning($"[DuskersCoopMod] MainMenu.LaunchGameFinal threw ({ex.Message}), falling back to direct scene load.");
                            }
                        }

                        // Direct scene load fallback (guaranteed to launch regardless of UI menu stack state)
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
                        Debug.Log($"[DuskersCoopMod] [v1.1.1] BOARD_DUNGEON received from host: '{rawTarget}'");
                        string targetName = rawTarget;
                        string targetGroup = "";
                        if (rawTarget.Contains("|"))
                        {
                            var parts = rawTarget.Split('|');
                            targetName = parts[0];
                            targetGroup = parts.Length > 1 ? parts[1] : "";
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

        public void ExecuteAuthoritativeCommand(string command)
        {
            if (ConsoleWindow3.Instance != null)
            {
                try
                {
                    ConsoleWindow3.Instance.InjectCommandText(command);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[DuskersCoopMod] Error executing remote command '{command}': {ex}");
                }
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
