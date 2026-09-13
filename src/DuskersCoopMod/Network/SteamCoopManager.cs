using System;
using System.Collections.Generic;
using System.Text;
using DuskersCoopMod.UI;
using Steamworks;
using UnityEngine;

namespace DuskersCoopMod.Network
{
    public class SteamConnectedOperator
    {
        public CSteamID SteamId;
        public string Name;
        public int Id;
        public string Version = "Unknown";
    }

    public class SteamCoopManager : MonoBehaviour
    {
        public static SteamCoopManager Instance { get; private set; }

        public bool IsSteamActive => SteamAPI.IsSteamRunning();
        public CSteamID CurrentLobbyId { get; private set; } = CSteamID.Nil;
        public CSteamID HostSteamId { get; private set; } = CSteamID.Nil;

        public bool IsSteamHost => HostSteamId != CSteamID.Nil && HostSteamId == SteamUser.GetSteamID();
        public bool IsSteamClient => HostSteamId != CSteamID.Nil && HostSteamId != SteamUser.GetSteamID();

        private readonly List<SteamConnectedOperator> _steamClients = new List<SteamConnectedOperator>();
        private int _operatorCounter = 2;

        private Callback<LobbyCreated_t> _lobbyCreated;
        private Callback<GameLobbyJoinRequested_t> _lobbyJoinRequested;
        private Callback<LobbyEnter_t> _lobbyEntered;
        private Callback<P2PSessionRequest_t> _p2pSessionRequest;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                InitCallbacks();
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void InitCallbacks()
        {
            if (!IsSteamActive) return;

            try
            {
                _lobbyCreated = Callback<LobbyCreated_t>.Create(OnLobbyCreated);
                _lobbyJoinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnLobbyJoinRequested);
                _lobbyEntered = Callback<LobbyEnter_t>.Create(OnLobbyEntered);
                _p2pSessionRequest = Callback<P2PSessionRequest_t>.Create(OnP2PSessionRequest);

                SteamNetworking.AllowP2PPacketRelay(true);
                Debug.Log("[DuskersCoopMod] Steamworks P2P Networking & Lobby callbacks registered.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DuskersCoopMod] Failed to register Steam callbacks: " + ex.Message);
            }
        }

        public void CreateHostLobby()
        {
            LeaveLobby();
            HostSteamId = SteamUser.GetSteamID();
            _steamClients.Clear();
            _operatorCounter = 2;

            // Start TCP Host as local fallback (allows GreenLuma LAN / Radmin and Steam P2P simultaneously)
            CoopNetworkManager.Instance.StartHost();

            if (!IsSteamActive)
            {
                CoopNetworkManager.Instance.PrintToLocalConsole("[COOP] Steam is not running. Local IP/Port server active.", ConsoleMessageType.Warning);
                return;
            }

            // Use Public lobby so GreenLuma accounts can join without Valve master-server license verification
            SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePublic, 4);
        }

        private void OnLobbyCreated(LobbyCreated_t param)
        {
            if (param.m_eResult == EResult.k_EResultOK)
            {
                CurrentLobbyId = new CSteamID(param.m_ulSteamIDLobby);
                string persona = SteamFriends.GetPersonaName();
                SteamMatchmaking.SetLobbyData(CurrentLobbyId, "name", $"{persona}'s Duskers Bridge");
                SteamMatchmaking.SetLobbyData(CurrentLobbyId, "appid", "254320");
                SteamMatchmaking.SetLobbyData(CurrentLobbyId, "version", CoopNetworkManager.MOD_VERSION);
                SteamMatchmaking.SetLobbyData(CurrentLobbyId, "host_steamid", HostSteamId.m_SteamID.ToString());
                SteamMatchmaking.SetLobbyJoinable(CurrentLobbyId, true);

                Debug.Log($"[DuskersCoopMod] Steam Lobby created: {CurrentLobbyId}. Shift+Tab invites enabled!");
                CoopNetworkManager.Instance.PrintToLocalConsole($"[COOP] Steam Lobby created for {persona}! Shift+Tab invites ready.", ConsoleMessageType.SpecialInfo);
            }
            else
            {
                Debug.LogWarning($"[DuskersCoopMod] Failed to create Steam lobby: {param.m_eResult}");
            }
        }

        public void OpenInviteOverlay()
        {
            if (!IsSteamActive)
            {
                DialogUI.Instance.ShowDialog(
                    "Steam Not Available",
                    "Steamworks is not initialized or running in offline mode.",
                    ModalWindowType.OK,
                    null
                );
                return;
            }

            if (CurrentLobbyId != CSteamID.Nil)
            {
                SteamFriends.ActivateGameOverlayInviteDialog(CurrentLobbyId);
            }
            else
            {
                SteamFriends.ActivateGameOverlay("Friends");
            }
        }

        private void OnP2PSessionRequest(P2PSessionRequest_t param)
        {
            Debug.Log($"[DuskersCoopMod] Accepting Steam P2P session from: {param.m_steamIDRemote}");
            SteamNetworking.AcceptP2PSessionWithUser(param.m_steamIDRemote);
        }

        private void OnLobbyJoinRequested(GameLobbyJoinRequested_t param)
        {
            Debug.Log($"[DuskersCoopMod] Steam Lobby join requested by friend! Joining lobby: {param.m_steamIDLobby}");
            SteamMatchmaking.JoinLobby(param.m_steamIDLobby);
        }

        private void OnLobbyEntered(LobbyEnter_t param)
        {
            CSteamID lobby = new CSteamID(param.m_ulSteamIDLobby);
            CSteamID owner = SteamMatchmaking.GetLobbyOwner(lobby);
            CSteamID myId = SteamUser.GetSteamID();

            CurrentLobbyId = lobby;
            HostSteamId = owner;

            // If we are the Host, do nothing further (server is listening)
            if (owner == myId)
            {
                Debug.Log("[DuskersCoopMod] Entered own Steam Lobby as Host. Ready for friends.");
                return;
            }

            // Guest connects to Host via Steam P2P
            Debug.Log($"[DuskersCoopMod] Guest entered Steam Lobby! Connecting to host Steam ID: {owner}");
            CoopNetworkManager.Instance.Role = NetworkRole.Client;

            SteamNetworking.AcceptP2PSessionWithUser(owner);

            string myPersona = SteamFriends.GetPersonaName();
            string helloPacket = PacketWrapper.Create("STEAM_HELLO", myPersona, new HandshakeData
            {
                playerName = myPersona,
                version = CoopNetworkManager.MOD_VERSION
            });

            SendP2PToHost(helloPacket);

            string hostName = SteamFriends.GetFriendPersonaName(owner);
            if (string.IsNullOrEmpty(hostName)) hostName = "Host Bridge";
            CoopNetworkManager.Instance.PrintToLocalConsole($"[COOP] Connecting to {hostName}'s Bridge via Steam P2P...", ConsoleMessageType.SpecialInfo);

            MenuPanelUI.Instance.Clear();
            MenuPanelUI.Instance.Reset();
            new CoopConnectingScreen(hostName, owner);
        }

        public void ConnectToHostBySteamId(CSteamID hostSteamId)
        {
            if (!IsSteamActive)
            {
                DialogUI.Instance?.ShowDialog(
                    "Steam Not Available",
                    "Steamworks is not initialized. Please ensure Steam or GreenLuma is running.",
                    ModalWindowType.OK,
                    null
                );
                return;
            }

            LeaveLobby();
            HostSteamId = hostSteamId;
            CoopNetworkManager.Instance.Role = NetworkRole.Client;

            SteamNetworking.AcceptP2PSessionWithUser(hostSteamId);

            string myPersona = SteamFriends.GetPersonaName();
            string helloPacket = PacketWrapper.Create("STEAM_HELLO", myPersona, new HandshakeData
            {
                playerName = myPersona,
                version = CoopNetworkManager.MOD_VERSION
            });

            SendP2PToHost(helloPacket);

            string hostName = SteamFriends.GetFriendPersonaName(hostSteamId);
            if (string.IsNullOrEmpty(hostName)) hostName = $"Host ({hostSteamId.m_SteamID})";

            CoopNetworkManager.Instance.PrintToLocalConsole($"[COOP] Connecting to {hostName}'s Bridge via Steam P2P...", ConsoleMessageType.SpecialInfo);

            MenuPanelUI.Instance.Clear();
            MenuPanelUI.Instance.Reset();
            new CoopConnectingScreen(hostName, hostSteamId);
        }

        private void Update()
        {
            if (!IsSteamActive) return;

            try { SteamAPI.RunCallbacks(); } catch { }

            uint size;
            while (SteamNetworking.IsP2PPacketAvailable(out size, 0))
            {
                byte[] buffer = new byte[size];
                uint bytesRead;
                CSteamID remoteId;
                if (SteamNetworking.ReadP2PPacket(buffer, size, out bytesRead, out remoteId, 0))
                {
                    string json = Encoding.UTF8.GetString(buffer, 0, (int)bytesRead);
                    PacketWrapper packet = PacketWrapper.FromJson(json);
                    if (packet != null)
                    {
                        ProcessSteamPacket(packet, remoteId);
                    }
                }
            }
        }

        private void ProcessSteamPacket(PacketWrapper packet, CSteamID senderId)
        {
            if (IsSteamHost)
            {
                SteamConnectedOperator op = _steamClients.Find(c => c.SteamId == senderId);
                if (op == null)
                {
                    string persona = SteamFriends.GetFriendPersonaName(senderId);
                    if (string.IsNullOrEmpty(persona)) persona = $"Operator {_operatorCounter}";
                    op = new SteamConnectedOperator
                    {
                        SteamId = senderId,
                        Name = persona,
                        Id = _operatorCounter++
                    };
                    _steamClients.Add(op);

                    CoopNetworkManager.Instance.PrintToLocalConsole($"[COOP] {op.Name} joined your Steam Bridge! (Total: {_steamClients.Count + 1} operators)", ConsoleMessageType.SpecialInfo);

                    string handshake = PacketWrapper.Create("HANDSHAKE", "Host", new HandshakeData
                    {
                        playerName = op.Name,
                        version = CoopNetworkManager.MOD_VERSION
                    });
                    SendP2PTo(senderId, handshake);

                    // Synchronize Host's active save so guest shares the exact same galaxy, fleet and derelicts
                    string saveBase64 = DuskersCoopMod.Save.CoopSaveSyncManager.PackageActiveSave(DuskersCoopMod.Save.SaveSlotManager.CurrentSlot);
                    if (!string.IsNullOrEmpty(saveBase64))
                    {
                        string savePacket = PacketWrapper.Create("SAVE_SYNC", "Host", new SaveSyncData
                        {
                            compressedBase64 = saveBase64
                        });
                        SendP2PTo(senderId, savePacket);
                    }

                    DuskersCoopMod.Save.CoopGalaxySyncManager.BroadcastGalaxyState();

                    BroadcastP2P(PacketWrapper.Create("SYSTEM_LOG", "Host", $"[COOP] {op.Name} joined the command bridge!"), senderId);
                }

                if (packet.type == "STEAM_HELLO")
                {
                    var helloData = packet.GetData<HandshakeData>();
                    string clientVer = (helloData != null && !string.IsNullOrEmpty(helloData.version)) ? helloData.version : "Unknown";
                    op.Version = clientVer;

                    if (!string.Equals(clientVer, CoopNetworkManager.MOD_VERSION, StringComparison.OrdinalIgnoreCase))
                    {
                        CoopNetworkManager.Instance.PrintToLocalConsole($"[COOP CRITICAL] VERSION MISMATCH! You are Host (v{CoopNetworkManager.MOD_VERSION}), but {op.Name} connected with v{clientVer}! Desyncs and bugs will occur! Both players MUST use v{CoopNetworkManager.MOD_VERSION}.", ConsoleMessageType.Warning);
                        try
                        {
                            DialogUI.Instance?.ShowDialog(
                                "COOP VERSION MISMATCH",
                                $"Operator '{op.Name}' joined with mod version v{clientVer}, but you are running v{CoopNetworkManager.MOD_VERSION}!\n\nGameplay desyncs will occur. Both operators must update to the same version.",
                                ModalWindowType.OK,
                                null
                            );
                        }
                        catch { }

                        string mismatchPacket = PacketWrapper.Create("VERSION_MISMATCH", "Host", new HandshakeData
                        {
                            playerName = "Host",
                            version = CoopNetworkManager.MOD_VERSION
                        });
                        SendP2PTo(senderId, mismatchPacket);
                    }
                }

                packet.sender = op.Name;
                CoopNetworkManager.Instance.EnqueueIncoming(packet);

                if (packet.type == "COMMAND")
                {
                    var cmdData = packet.GetData<CommandData>();
                    if (cmdData != null)
                    {
                        string echo = PacketWrapper.Create("CONSOLE_TEXT", op.Name, new ConsoleTextData
                        {
                            text = $"[{op.Name}] > {cmdData.command}",
                            messageType = (int)ConsoleMessageType.Info,
                            messageFormat = (int)ConsoleMessageFormat.Normal
                        });
                        BroadcastP2P(echo, senderId);
                    }
                }
            }
            else
            {
                CoopNetworkManager.Instance.EnqueueIncoming(packet);
            }
        }

        public void SendP2PTo(CSteamID target, string json)
        {
            if (!IsSteamActive || target == CSteamID.Nil) return;
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            SteamNetworking.SendP2PPacket(target, bytes, (uint)bytes.Length, EP2PSend.k_EP2PSendReliable, 0);
        }

        public void SendP2PToHost(string json)
        {
            if (HostSteamId != CSteamID.Nil)
            {
                SendP2PTo(HostSteamId, json);
            }
        }

        public void BroadcastP2P(string json, CSteamID exclude = default(CSteamID))
        {
            if (!IsSteamHost) return;
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            foreach (var op in _steamClients)
            {
                if (op.SteamId != exclude)
                {
                    SteamNetworking.SendP2PPacket(op.SteamId, bytes, (uint)bytes.Length, EP2PSend.k_EP2PSendReliable, 0);
                }
            }
        }

        public List<string> GetSteamOperatorsList()
        {
            List<string> list = new List<string>();
            if (IsSteamHost)
            {
                string hostName = SteamFriends.GetPersonaName();
                list.Add($"Operator 1 ({hostName} - Host) [v{CoopNetworkManager.MOD_VERSION}]");
                foreach (var op in _steamClients)
                {
                    bool mismatch = !string.Equals(op.Version, CoopNetworkManager.MOD_VERSION, StringComparison.OrdinalIgnoreCase);
                    string status = mismatch ? $" [v{op.Version} - INCOMPATIBLE!]" : $" [v{op.Version}]";
                    list.Add($" - {op.Name}{status}");
                }
            }
            else if (IsSteamClient)
            {
                string hostName = SteamFriends.GetFriendPersonaName(HostSteamId);
                string hVer = !string.IsNullOrEmpty(CoopNetworkManager.Instance?.HostVersion) ? CoopNetworkManager.Instance.HostVersion : "Unknown";
                bool mismatch = !string.Equals(hVer, CoopNetworkManager.MOD_VERSION, StringComparison.OrdinalIgnoreCase);
                string note = mismatch ? " [INCOMPATIBLE VERSION!]" : "";
                list.Add($"Connected to {hostName}'s Bridge (Host: v{hVer}, You: v{CoopNetworkManager.MOD_VERSION}){note}");
            }
            return list;
        }

        public int SteamConnectedCount => IsSteamHost ? _steamClients.Count : (IsSteamClient ? 1 : 0);

        public void LeaveLobby()
        {
            if (IsSteamActive)
            {
                if (CurrentLobbyId != CSteamID.Nil)
                {
                    SteamMatchmaking.LeaveLobby(CurrentLobbyId);
                    CurrentLobbyId = CSteamID.Nil;
                }
                foreach (var c in _steamClients)
                {
                    SteamNetworking.CloseP2PSessionWithUser(c.SteamId);
                }
                _steamClients.Clear();
                HostSteamId = CSteamID.Nil;
            }
        }

        public struct SteamFriendBridgeInfo
        {
            public CSteamID SteamId;
            public string Name;
            public CSteamID LobbyId;
        }

        public List<SteamFriendBridgeInfo> GetFriendsPlayingDuskers()
        {
            var list = new List<SteamFriendBridgeInfo>();
            if (!IsSteamActive) return list;

            int count = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
            AppId_t myAppId = SteamUtils.GetAppID();

            for (int i = 0; i < count; i++)
            {
                CSteamID friendId = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
                FriendGameInfo_t gameInfo;
                if (SteamFriends.GetFriendGamePlayed(friendId, out gameInfo))
                {
                    if (gameInfo.m_gameID.AppID() == myAppId)
                    {
                        list.Add(new SteamFriendBridgeInfo
                        {
                            SteamId = friendId,
                            Name = SteamFriends.GetFriendPersonaName(friendId),
                            LobbyId = gameInfo.m_steamIDLobby
                        });
                    }
                }
            }
            return list;
        }
    }
}
