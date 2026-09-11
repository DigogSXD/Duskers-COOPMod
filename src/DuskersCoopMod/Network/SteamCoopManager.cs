using System;
using DuskersCoopMod.UI;
using Steamworks;
using UnityEngine;

namespace DuskersCoopMod.Network
{
    public class SteamCoopManager : MonoBehaviour
    {
        public static SteamCoopManager Instance { get; private set; }

        public bool IsSteamActive => SteamAPI.IsSteamRunning();
        public CSteamID CurrentLobbyId { get; private set; } = CSteamID.Nil;

        private Callback<LobbyCreated_t> _lobbyCreated;
        private Callback<GameLobbyJoinRequested_t> _lobbyJoinRequested;
        private Callback<LobbyEnter_t> _lobbyEntered;

        private string _pendingIp;
        private int _pendingPort;
        private string _pendingCode;

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
                Debug.Log("[DuskersCoopMod] Steamworks Lobby & Invite callbacks registered successfully.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DuskersCoopMod] Failed to register Steam callbacks: " + ex.Message);
            }
        }

        public void CreateLobby(string hostIp, int hostPort, string sessionCode)
        {
            if (!IsSteamActive) return;

            _pendingIp = hostIp;
            _pendingPort = hostPort;
            _pendingCode = sessionCode;

            // Leave any previous lobby
            LeaveLobby();

            SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, 4);
        }

        private void OnLobbyCreated(LobbyCreated_t param)
        {
            if (param.m_eResult == EResult.k_EResultOK)
            {
                CurrentLobbyId = new CSteamID(param.m_ulSteamIDLobby);
                SteamMatchmaking.SetLobbyData(CurrentLobbyId, "ip", _pendingIp ?? "");
                SteamMatchmaking.SetLobbyData(CurrentLobbyId, "port", _pendingPort.ToString());
                SteamMatchmaking.SetLobbyData(CurrentLobbyId, "code", _pendingCode ?? "");
                SteamMatchmaking.SetLobbyData(CurrentLobbyId, "name", $"{SteamFriends.GetPersonaName()}'s Duskers Bridge");
                SteamMatchmaking.SetLobbyJoinable(CurrentLobbyId, true);

                Debug.Log($"[DuskersCoopMod] Steam Lobby created: {CurrentLobbyId}. Shift+Tab invites enabled!");
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

            // If we are the Host, do NOT connect to ourselves or disconnect the server!
            if (owner == myId || CoopNetworkManager.Instance.Role == NetworkRole.Host)
            {
                Debug.Log("[DuskersCoopMod] Entered own Steam Lobby as Host. Server listening and waiting for operators.");
                return;
            }

            string ip = SteamMatchmaking.GetLobbyData(lobby, "ip");
            string portStr = SteamMatchmaking.GetLobbyData(lobby, "port");
            string code = SteamMatchmaking.GetLobbyData(lobby, "code");

            Debug.Log($"[DuskersCoopMod] Guest entered Steam Lobby! IP: {ip}, Port: {portStr}, Code: {code}");

            if (!string.IsNullOrEmpty(ip) && int.TryParse(portStr, out int port))
            {
                CoopNetworkManager.Instance.ConnectToHost(ip, port);
                MenuPanelUI.Instance.Clear();
                MenuPanelUI.Instance.Reset();
                new CoopMenuScreen();
            }
            else if (!string.IsNullOrEmpty(code) && SessionCodeHelper.Decode(code, out string decIp, out int decPort))
            {
                CoopNetworkManager.Instance.ConnectToHost(decIp, decPort);
                MenuPanelUI.Instance.Clear();
                MenuPanelUI.Instance.Reset();
                new CoopMenuScreen();
            }
        }

        public void LeaveLobby()
        {
            if (IsSteamActive && CurrentLobbyId != CSteamID.Nil)
            {
                SteamMatchmaking.LeaveLobby(CurrentLobbyId);
                CurrentLobbyId = CSteamID.Nil;
            }
        }
    }
}
