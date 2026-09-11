using System;
using DuskersCoopMod.Network;
using Steamworks;
using UnityEngine;

namespace DuskersCoopMod.UI
{
    public class CoopConnectingScreen : MenuScreenClass
    {
        private readonly string _hostName;
        private readonly CSteamID _hostSteamId;
        private float _elapsedTime = 0f;
        private bool _connected = false;
        private bool _saveSynced = false;
        private float _transitionTimer = 0f;
        private int _lastBarFilled = -1;

        public CoopConnectingScreen(string hostName, CSteamID hostSteamId) : base(null)
        {
            _hostName = string.IsNullOrEmpty(hostName) ? "Host Bridge" : hostName;
            _hostSteamId = hostSteamId;
            CoopNetworkManager.OnHandshakeReceived += HandleHandshake;
            CoopNetworkManager.OnSaveSynchronized += HandleSaveSync;
        }

        protected override void Initialize()
        {
            ActiveText = "Establishing Comms Link";
            IgnoreCancel = false;
        }

        private void HandleHandshake(string operatorName)
        {
            if (!_connected)
            {
                _connected = true;
                RefreshScreen();
            }
        }

        private void HandleSaveSync()
        {
            _saveSynced = true;
            RefreshScreen();
        }

        public override void LoadMenu()
        {
            MenuPanelUI.Instance.Clear();
            int num = 0;

            var header = new DuskersMenuItem("=== ESTABLISHING COMMS LINK ===", KeyCode.None, null, num++);
            header.Disabled = true;
            header.OverridenColor = Color.cyan;
            MenuPanelUI.Instance.AddMenuItem(header);

            var hostItem = new DuskersMenuItem($"Target Bridge: {_hostName}", KeyCode.None, null, num++);
            hostItem.Disabled = true;
            hostItem.OverridenColor = Color.green;
            MenuPanelUI.Instance.AddMenuItem(hostItem);

            var protoItem = new DuskersMenuItem("Protocol: Steamworks P2P Encrypted Relay (SDR)", KeyCode.None, null, num++);
            protoItem.Disabled = true;
            protoItem.OverridenColor = Color.yellow;
            MenuPanelUI.Instance.AddMenuItem(protoItem);

            MenuPanelUI.Instance.AddMenuItem(null);
            num++;

            // Progress Bar
            const int totalBlocks = 24;
            int pct = _connected ? 100 : Mathf.Min(95, (int)(_elapsedTime * 25f));
            int filled = Mathf.Clamp((pct * totalBlocks) / 100, 0, totalBlocks);
            string barInside;
            if (filled >= totalBlocks)
            {
                barInside = new string('=', totalBlocks);
            }
            else
            {
                int remaining = Math.Max(0, totalBlocks - filled - 1);
                barInside = new string('=', filled) + ">" + new string(' ', remaining);
            }
            string bar = $"[{barInside}] {pct}%";

            var barItem = new DuskersMenuItem(bar, KeyCode.None, null, num++);
            barItem.Disabled = true;
            barItem.OverridenColor = _connected ? Color.green : Color.yellow;
            MenuPanelUI.Instance.AddMenuItem(barItem);

            MenuPanelUI.Instance.AddMenuItem(null);
            num++;

            // Telemetry Log Lines
            if (_connected)
            {
                var okItem = new DuskersMenuItem("> LINK ESTABLISHED! ENCRYPTED TUNNEL ACTIVE.", KeyCode.None, null, num++);
                okItem.Disabled = true;
                okItem.OverridenColor = Color.green;
                MenuPanelUI.Instance.AddMenuItem(okItem);

                string syncText = _saveSynced ? "> Host Galaxy & Fleet synchronized! [SlotCoop Active]" : "> Synchronizing Host Galaxy & Fleet...";
                var readyItem = new DuskersMenuItem(syncText, KeyCode.None, null, num++);
                readyItem.Disabled = true;
                readyItem.OverridenColor = _saveSynced ? Color.green : Color.cyan;
                MenuPanelUI.Instance.AddMenuItem(readyItem);
            }
            else
            {
                string logLine = "> Initializing Steam Datagram Relay (SDR)...";
                if (_elapsedTime > 2.5f)
                {
                    logLine = "> Awaiting Bridge Operator Handshake...";
                }
                else if (_elapsedTime > 1.2f)
                {
                    logLine = "> Establishing Encrypted P2P Session with Host...";
                }

                var logItem = new DuskersMenuItem(logLine, KeyCode.None, null, num++);
                logItem.Disabled = true;
                logItem.OverridenColor = Color.yellow;
                MenuPanelUI.Instance.AddMenuItem(logItem);

                var standbyItem = new DuskersMenuItem("Please stand by...", KeyCode.None, null, num++);
                standbyItem.Disabled = true;
                MenuPanelUI.Instance.AddMenuItem(standbyItem);
            }

            MenuPanelUI.Instance.AddMenuItem(null);
            num++;

            // Cancel Button
            if (!_connected)
            {
                MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem("[C]ancel Connection (or Esc)", KeyCode.C, (m) =>
                {
                    CancelConnection();
                }, num++));
            }

            base.LoadMenu();
        }

        private void CancelConnection()
        {
            CoopNetworkManager.OnHandshakeReceived -= HandleHandshake;
            CoopNetworkManager.OnSaveSynchronized -= HandleSaveSync;
            SteamCoopManager.Instance?.LeaveLobby();
            CoopNetworkManager.Instance?.Disconnect();
            MenuPanelUI.Instance.Clear();
            MenuPanelUI.Instance.Reset();
            new CoopMenuScreen();
        }

        public override void Update()
        {
            base.Update();

            if (!_connected && Input.GetKeyDown(KeyCode.Escape))
            {
                CancelConnection();
                return;
            }

            _elapsedTime += Time.deltaTime;

            if (_connected)
            {
                _transitionTimer += Time.deltaTime;
                if (_transitionTimer >= 1.2f)
                {
                    CoopNetworkManager.OnHandshakeReceived -= HandleHandshake;
                    CoopNetworkManager.OnSaveSynchronized -= HandleSaveSync;
                    MenuPanelUI.Instance.Clear();
                    MenuPanelUI.Instance.Reset();
                    new CoopMenuScreen();
                    return;
                }
            }
            else
            {
                // Timeout after 20 seconds
                if (_elapsedTime > 20f)
                {
                    CoopNetworkManager.Instance?.PrintToLocalConsole("[COOP] Connection attempt timed out. Returning to menu.", ConsoleMessageType.Error);
                    CancelConnection();
                    return;
                }

                const int totalBlocks = 24;
                int pct = Mathf.Min(95, (int)(_elapsedTime * 25f));
                int currentFilled = Mathf.Clamp((pct * totalBlocks) / 100, 0, totalBlocks);
                if (currentFilled != _lastBarFilled)
                {
                    _lastBarFilled = currentFilled;
                    RefreshScreen();
                }
            }
        }

        public void RefreshScreen()
        {
            MenuPanelUI.Instance.Clear();
            MenuPanelUI.Instance.Reset();
            LoadMenu();
        }
    }
}
