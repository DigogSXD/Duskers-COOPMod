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
                MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem("[H]ost Session", KeyCode.H, OnHostSelected, num++));
                MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem("[J]oin Session", KeyCode.J, OnJoinSelected, num++));

                if (net != null && !string.IsNullOrEmpty(net.LastTargetIp))
                {
                    MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem($"[R]econnect ({net.LastTargetIp}:{net.LastTargetPort})", KeyCode.R, (m) =>
                    {
                        net.Reconnect();
                        RefreshScreen();
                    }, num++));
                }

                // Port option
                int portToShow = net != null ? net.CurrentPort : CoopNetworkManager.DEFAULT_PORT;
                MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem($"P[o]rt: {portToShow}", KeyCode.O, (m) =>
                {
                    new PortMenuScreen();
                }, num++));

                // Save slot option
                MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem($"[S]ave Slot: Slot {SaveSlotManager.CurrentSlot}", KeyCode.S, (m) =>
                {
                    new SaveSlotMenuScreen();
                }, num++));

                MenuPanelUI.Instance.AddMenuItem(null);
                num++;

                var tip = new DuskersMenuItem("Tip: Copy code (Ctrl+C), then press J", KeyCode.None, null, num++);
                tip.Disabled = true;
                MenuPanelUI.Instance.AddMenuItem(tip);

                var status = new DuskersMenuItem("Status: Offline (No session)", KeyCode.None, null, num++);
                status.Disabled = true;
                MenuPanelUI.Instance.AddMenuItem(status);
            }
            else if (isHost)
            {
                // Host State
                int hostPort = net != null ? net.CurrentPort : CoopNetworkManager.DEFAULT_PORT;
                string ip = SessionCodeHelper.GetPreferredLocalIp();
                string sessionCode = SessionCodeHelper.Encode(ip, hostPort);

                var header = new DuskersMenuItem("=== HOST SESSION ===", KeyCode.None, null, num++);
                header.Disabled = true;
                header.OverridenColor = Color.cyan;
                MenuPanelUI.Instance.AddMenuItem(header);

                var codeItem = new DuskersMenuItem($"Code: {sessionCode}", KeyCode.None, null, num++);
                codeItem.Disabled = true;
                codeItem.OverridenColor = Color.green;
                MenuPanelUI.Instance.AddMenuItem(codeItem);

                var portItem = new DuskersMenuItem($"Port: {hostPort}", KeyCode.None, null, num++);
                portItem.Disabled = true;
                portItem.OverridenColor = Color.yellow;
                MenuPanelUI.Instance.AddMenuItem(portItem);

                MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem("[C]opy Code", KeyCode.C, (m) =>
                {
                    GUIUtility.systemCopyBuffer = sessionCode;
                    DialogUI.Instance.ShowDialog(
                        "Code Copied",
                        $"Session Code:\r\n{sessionCode}\r\n\r\nCopied to clipboard!\r\nSend this to your friends on Discord/Steam.",
                        ModalWindowType.OK,
                        null
                    );
                }, num++));

                // Steam Invite Button
                MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem("[I]nvite via Steam (Shift+Tab)", KeyCode.I, (m) =>
                {
                    SteamCoopManager.Instance?.OpenInviteOverlay();
                }, num++));

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
                    var wait = new DuskersMenuItem(" - Waiting for friends...", KeyCode.None, null, num++);
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
                net.StartHost(net.CurrentPort);
                string ip = SessionCodeHelper.GetPreferredLocalIp();
                string sessionCode = SessionCodeHelper.Encode(ip, net.CurrentPort);
                SteamCoopManager.Instance?.CreateLobby(ip, net.CurrentPort, sessionCode);
                RefreshScreen();
            }
        }

        private void OnJoinSelected(DuskersMenuItem item)
        {
            new JoinChoiceMenuScreen();
        }
    }
}
