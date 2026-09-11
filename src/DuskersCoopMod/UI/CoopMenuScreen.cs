using System;
using System.Collections.Generic;
using System.Reflection;
using DuskersCoopMod.Network;
using DuskersCoopMod.Save;
using HarmonyLib;
using UnityEngine;

namespace DuskersCoopMod.UI
{
    public class CoopMenuScreen : MenuScreenClass
    {
        private int _lastConnectedCount = -1;

        public CoopMenuScreen() : base(null)
        {
        }

        protected override void Initialize()
        {
            ActiveText = "Multiplayer Operations";
            IgnoreCancel = false;
        }

        public override void LoadMenu()
        {
            MenuPanelUI.Instance.Clear();
            int num = 0;

            var net = CoopNetworkManager.Instance;
            bool isHost = net != null && net.Role == NetworkRole.Host;
            bool isClient = net != null && net.Role == NetworkRole.Client;
            int count = net != null ? net.ConnectedCount : 0;
            _lastConnectedCount = count;

            if (!isHost && !isClient)
            {
                // Standalone / Offline State
                MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem("[H]ost Session (Steam Lobby)", KeyCode.H, OnHostSelected, num++));
                MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem("[J]oin Session (Steam Friends)", KeyCode.J, OnJoinSelected, num++));

                // Save slot option
                MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem($"[S]ave Slot: Slot {SaveSlotManager.CurrentSlot}", KeyCode.S, (m) =>
                {
                    new SaveSlotMenuScreen();
                }, num++));

                MenuPanelUI.Instance.AddMenuItem(null);
                num++;

                var tip = new DuskersMenuItem("Steam Co-op: Invite friends via Shift+Tab or [I]nvite", KeyCode.None, null, num++);
                tip.Disabled = true;
                MenuPanelUI.Instance.AddMenuItem(tip);

                var status = new DuskersMenuItem("Status: Ready (Steamworks Active)", KeyCode.None, null, num++);
                status.Disabled = true;
                MenuPanelUI.Instance.AddMenuItem(status);
            }
            else if (isHost)
            {
                // Host State
                var header = new DuskersMenuItem("=== HOST SESSION (STEAM LOBBY) ===", KeyCode.None, null, num++);
                header.Disabled = true;
                header.OverridenColor = Color.cyan;
                MenuPanelUI.Instance.AddMenuItem(header);

                // Steam Invite Button
                MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem("[I]nvite Friends via Steam (Shift+Tab)", KeyCode.I, (m) =>
                {
                    SteamCoopManager.Instance?.OpenInviteOverlay();
                }, num++));

                if (SteamCoopManager.Instance != null && SteamCoopManager.Instance.IsSteamActive)
                {
                    ulong steamId = Steamworks.SteamUser.GetSteamID().m_SteamID;
                    MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem("[C]opy Bridge Steam ID (GreenLuma)", KeyCode.C, (m) =>
                    {
                        GUIUtility.systemCopyBuffer = steamId.ToString();
                        DialogUI.Instance.ShowDialog(
                            "Steam Bridge ID Copied",
                            $"Your Steam ID has been copied to clipboard:\r\n\r\n{steamId}\r\n\r\nYour friend can use [J]oin Session -> [P] Fast Join from Clipboard to connect directly via Steam P2P!",
                            ModalWindowType.OK,
                            null
                        );
                    }, num++));
                }

                var slotDisplay = new DuskersMenuItem($"Active Save: Slot {SaveSlotManager.CurrentSlot}", KeyCode.None, null, num++);
                slotDisplay.Disabled = true;
                MenuPanelUI.Instance.AddMenuItem(slotDisplay);

                MenuPanelUI.Instance.AddMenuItem(null);
                num++;

                // List connected operators
                List<string> ops = net.GetConnectedOperatorsList();
                int totalOps = ops.Count;

                var countItem = new DuskersMenuItem($"Bridge Operators ({totalOps}):", KeyCode.None, null, num++);
                countItem.Disabled = true;
                countItem.OverridenColor = totalOps > 1 ? Color.green : Color.yellow;
                MenuPanelUI.Instance.AddMenuItem(countItem);

                foreach (var opName in ops)
                {
                    var opItem = new DuskersMenuItem($" - {opName}", KeyCode.None, null, num++);
                    opItem.Disabled = true;
                    MenuPanelUI.Instance.AddMenuItem(opItem);
                }

                if (totalOps <= 1)
                {
                    var wait = new DuskersMenuItem(" - Waiting for friends to accept invite...", KeyCode.None, null, num++);
                    wait.Disabled = true;
                    wait.OverridenColor = Color.yellow;
                    MenuPanelUI.Instance.AddMenuItem(wait);
                }

                MenuPanelUI.Instance.AddMenuItem(null);
                num++;

                // Launch game
                MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem("[P]lay Game (Start Mission)", KeyCode.P, (m) =>
                {
                    LaunchGameSafely();
                }, num++));

                MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem("[D]isconnect / Stop Server", KeyCode.D, (m) =>
                {
                    SteamCoopManager.Instance?.LeaveLobby();
                    net.Disconnect();
                    RefreshScreen();
                }, num++));
            }
            else if (isClient)
            {
                // Client State
                var header = new DuskersMenuItem("=== REMOTE OPERATOR SESSION ===", KeyCode.None, null, num++);
                header.Disabled = true;
                header.OverridenColor = Color.cyan;
                MenuPanelUI.Instance.AddMenuItem(header);

                bool isConn = net.IsConnected;
                string clientStatus = isConn
                    ? $"[CONNECTED] Host: {net.RemoteEndpointInfo}"
                    : "Connecting to Host...";

                var statusItem = new DuskersMenuItem(clientStatus, KeyCode.None, null, num++);
                statusItem.Disabled = true;
                statusItem.OverridenColor = isConn ? Color.green : Color.yellow;
                MenuPanelUI.Instance.AddMenuItem(statusItem);

                var tip = new DuskersMenuItem("Shared Terminal active. All operators share fleet control.", KeyCode.None, null, num++);
                tip.Disabled = true;
                MenuPanelUI.Instance.AddMenuItem(tip);

                MenuPanelUI.Instance.AddMenuItem(null);
                num++;

                MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem("[P]lay Game (Enter Terminal)", KeyCode.P, (m) =>
                {
                    LaunchGameSafely();
                }, num++));

                MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem("[D]isconnect", KeyCode.D, (m) =>
                {
                    net.Disconnect();
                    RefreshScreen();
                }, num++));
            }

            base.LoadMenu();
        }

        private void LaunchGameSafely()
        {
            try
            {
                var net = CoopNetworkManager.Instance;
                if (net != null && net.Role == NetworkRole.Host && net.ConnectedCount > 0)
                {
                    Save.CoopSaveSyncManager.SyncToAllClients();
                    net.BroadcastPacket(PacketWrapper.Create("STRATEGIC_ACTION", "Host", new StrategicActionData
                    {
                        action = "LAUNCH_GAME"
                    }));
                }

                if (MainMenu.Instance != null)
                {
                    MethodInfo playMethod = AccessTools.Method(typeof(MainMenu), "MenuPlayGame", new Type[] { typeof(DuskersMenuItem) });
                    if (playMethod != null)
                    {
                        playMethod.Invoke(MainMenu.Instance, new object[] { null });
                        return;
                    }
                }
                Debug.LogError("[DuskersCoopMod] Could not find MenuPlayGame method on MainMenu");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DuskersCoopMod] Error launching game: {ex}");
            }
        }

        public override void Update()
        {
            base.Update();

            var net = CoopNetworkManager.Instance;
            int current = net != null ? net.ConnectedCount : 0;
            if (current != _lastConnectedCount)
            {
                _lastConnectedCount = current;
                RefreshScreen();
            }
        }

        public void RefreshScreen()
        {
            MenuPanelUI.Instance.Clear();
            MenuPanelUI.Instance.Reset();
            LoadMenu();
        }

        private void OnHostSelected(DuskersMenuItem item)
        {
            var net = CoopNetworkManager.Instance;
            if (net != null)
            {
                if (SteamCoopManager.Instance != null && SteamCoopManager.Instance.IsSteamActive)
                {
                    SteamCoopManager.Instance.CreateHostLobby();
                }
                else
                {
                    net.StartHost();
                }
                RefreshScreen();
            }
        }

        private void OnJoinSelected(DuskersMenuItem item)
        {
            new JoinChoiceMenuScreen();
        }
    }
}
