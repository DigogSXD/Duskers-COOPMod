using System;
using System.Net;
using DuskersCoopMod.Network;
using UnityEngine;

namespace DuskersCoopMod.UI
{
    public class JoinIpPortMenuScreen : MenuScreenClass
    {
        private string _inputBuffer = "";
        private string _parsedIp = "";
        private int _parsedPort = CoopNetworkManager.DEFAULT_PORT;
        private bool _isValid = false;
        private string _statusMsg = "";

        public JoinIpPortMenuScreen() : base(null)
        {
        }

        protected override void Initialize()
        {
            ActiveText = "Direct IP Join";
            IgnoreCancel = false;

            var net = CoopNetworkManager.Instance;
            int defPort = net != null ? net.CurrentPort : CoopNetworkManager.DEFAULT_PORT;

            // Check clipboard first
            string clip = (GUIUtility.systemCopyBuffer ?? string.Empty).Trim();
            if (TryParseAddress(clip, defPort, out _parsedIp, out _parsedPort))
            {
                _inputBuffer = clip;
                _isValid = true;
            }
            else if (net != null && !string.IsNullOrEmpty(net.LastTargetIp))
            {
                _parsedIp = net.LastTargetIp;
                _parsedPort = net.LastTargetPort;
                _inputBuffer = $"{_parsedIp}:{_parsedPort}";
                _isValid = true;
            }
            else
            {
                _inputBuffer = $"127.0.0.1:{defPort}";
                TryParseAddress(_inputBuffer, defPort, out _parsedIp, out _parsedPort);
                _isValid = true;
            }
        }

        private static bool TryParseAddress(string raw, int defaultPort, out string ip, out int port)
        {
            ip = "";
            port = defaultPort;

            if (string.IsNullOrEmpty(raw)) return false;

            string clean = raw.Trim();

            // Handle IP:PORT format
            if (clean.Contains(":"))
            {
                string[] parts = clean.Split(':');
                if (parts.Length == 2 && IPAddress.TryParse(parts[0], out _) && int.TryParse(parts[1], out int p))
                {
                    if (p >= 1024 && p <= 65535)
                    {
                        ip = parts[0];
                        port = p;
                        return true;
                    }
                }
                return false;
            }

            // Handle IP only format
            if (IPAddress.TryParse(clean, out _))
            {
                ip = clean;
                port = defaultPort;
                return true;
            }

            return false;
        }

        public override void LoadMenu()
        {
            MenuPanelUI.Instance.Clear();
            int num = 0;

            var header = new DuskersMenuItem("=== DIRECT IP:PORT JOIN ===", KeyCode.None, null, num++);
            header.Disabled = true;
            header.OverridenColor = Color.cyan;
            MenuPanelUI.Instance.AddMenuItem(header);

            // Connect button if valid
            if (_isValid)
            {
                MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem($"[C]onnect to {_parsedIp}:{_parsedPort}", KeyCode.C, (m) =>
                {
                    ConnectNow();
                }, num++));
            }
            else
            {
                var invalidItem = new DuskersMenuItem("Enter valid IP:PORT (e.g. 191.176.201.51:9600)", KeyCode.None, null, num++);
                invalidItem.Disabled = true;
                invalidItem.OverridenColor = Color.red;
                MenuPanelUI.Instance.AddMenuItem(invalidItem);
            }

            MenuPanelUI.Instance.AddMenuItem(null);
            num++;

            // Input buffer
            string display = string.IsNullOrEmpty(_inputBuffer) ? "<type IP:PORT or press P>" : _inputBuffer + "_";
            var bufItem = new DuskersMenuItem($"Address: [ {display} ]", KeyCode.None, null, num++);
            bufItem.Disabled = true;
            bufItem.OverridenColor = _isValid ? Color.green : Color.yellow;
            MenuPanelUI.Instance.AddMenuItem(bufItem);

            // Paste button
            string clip = (GUIUtility.systemCopyBuffer ?? string.Empty).Trim();
            string clipShort = clip.Length > 22 ? clip.Substring(0, 22) + "..." : clip;
            string pasteText = string.IsNullOrEmpty(clip)
                ? "[P]aste from Clipboard"
                : $"[P]aste Clipboard ({clipShort})";

            MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem(pasteText, KeyCode.P, (m) =>
            {
                PasteClipboard();
            }, num++));

            if (!string.IsNullOrEmpty(_inputBuffer))
            {
                MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem("[X] Clear Address", KeyCode.X, (m) =>
                {
                    _inputBuffer = "";
                    _isValid = false;
                    _statusMsg = "";
                    RefreshScreen();
                }, num++));
            }

            if (!string.IsNullOrEmpty(_statusMsg))
            {
                MenuPanelUI.Instance.AddMenuItem(null);
                num++;
                var stat = new DuskersMenuItem(_statusMsg, KeyCode.None, null, num++);
                stat.Disabled = true;
                stat.OverridenColor = _isValid ? Color.green : Color.red;
                MenuPanelUI.Instance.AddMenuItem(stat);
            }

            base.LoadMenu();
        }

        private void PasteClipboard()
        {
            string clip = (GUIUtility.systemCopyBuffer ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(clip))
            {
                _statusMsg = "Clipboard is empty.";
                RefreshScreen();
                return;
            }

            var net = CoopNetworkManager.Instance;
            int defPort = net != null ? net.CurrentPort : CoopNetworkManager.DEFAULT_PORT;

            _inputBuffer = clip;
            if (TryParseAddress(_inputBuffer, defPort, out _parsedIp, out _parsedPort))
            {
                _isValid = true;
                _statusMsg = $"Valid Address: {_parsedIp}:{_parsedPort}";
            }
            else
            {
                _isValid = false;
                _statusMsg = $"Invalid format. Use IP:PORT (e.g. 191.176.201.51:9600)";
            }
            RefreshScreen();
        }

        private void ConnectNow()
        {
            if (_isValid)
            {
                CoopNetworkManager.Instance.ConnectToHost(_parsedIp, _parsedPort);
                MenuPanelUI.Instance.Clear();
                MenuPanelUI.Instance.Reset();
                new CoopMenuScreen();
            }
        }

        public override void Update()
        {
            base.Update();

            bool changed = false;

            if (!string.IsNullOrEmpty(Input.inputString))
            {
                foreach (char c in Input.inputString)
                {
                    if (char.IsDigit(c) || c == '.' || c == ':')
                    {
                        if (_inputBuffer.Length < 28)
                        {
                            _inputBuffer += c;
                            changed = true;
                        }
                    }
                    else if (c == '\b' && _inputBuffer.Length > 0)
                    {
                        _inputBuffer = _inputBuffer.Substring(0, _inputBuffer.Length - 1);
                        changed = true;
                    }
                }
            }

            if (Input.GetKeyDown(KeyCode.Backspace) && _inputBuffer.Length > 0 && !changed)
            {
                _inputBuffer = _inputBuffer.Substring(0, _inputBuffer.Length - 1);
                changed = true;
            }

            if (changed)
            {
                var net = CoopNetworkManager.Instance;
                int defPort = net != null ? net.CurrentPort : CoopNetworkManager.DEFAULT_PORT;
                _isValid = TryParseAddress(_inputBuffer, defPort, out _parsedIp, out _parsedPort);
                _statusMsg = _isValid ? $"Ready: {_parsedIp}:{_parsedPort}" : "Type e.g. 191.176.201.51:9600";
                RefreshScreen();
                return;
            }

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                if (_isValid)
                {
                    ConnectNow();
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
