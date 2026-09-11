using System;
using System.Net;
using DuskersCoopMod.Network;
using UnityEngine;

namespace DuskersCoopMod.UI
{
    public class JoinIpPortMenuScreen : MenuScreenClass
    {
        private string _targetIp = "127.0.0.1";
        private int _targetPort = CoopNetworkManager.DEFAULT_PORT;
        private string _ipBuffer = "";
        private string _statusMsg = "";
        private Color _statusColor = Color.green;

        public JoinIpPortMenuScreen() : base(null)
        {
        }

        protected override void Initialize()
        {
            ActiveText = "Join via Direct IP + Port";
            IgnoreCancel = false;

            var net = CoopNetworkManager.Instance;
            if (net != null)
            {
                if (!string.IsNullOrEmpty(net.LastTargetIp))
                {
                    _targetIp = net.LastTargetIp;
                    _targetPort = net.LastTargetPort;
                }
                else
                {
                    _targetPort = net.CurrentPort;
                }
            }

            // Check if clipboard has an IP or IP:Port
            string clip = (GUIUtility.systemCopyBuffer ?? string.Empty).Trim();
            ParseAndApplyClipboard(clip, false);
        }

        private bool ParseAndApplyClipboard(string text, bool verbose = true)
        {
            if (string.IsNullOrEmpty(text)) return false;

            if (text.Contains(":"))
            {
                string[] parts = text.Split(':');
                if (IPAddress.TryParse(parts[0], out _) && int.TryParse(parts[1], out int p) && p >= 1024 && p <= 65535)
                {
                    _targetIp = parts[0];
                    _targetPort = p;
                    _ipBuffer = _targetIp;
                    if (verbose)
                    {
                        _statusMsg = $"Loaded IP {_targetIp} and Port {_targetPort} from clipboard!";
                        _statusColor = Color.green;
                    }
                    return true;
                }
            }

            if (IPAddress.TryParse(text, out _))
            {
                _targetIp = text;
                _ipBuffer = _targetIp;
                if (verbose)
                {
                    _statusMsg = $"Loaded IP {_targetIp} from clipboard!";
                    _statusColor = Color.green;
                }
                return true;
            }

            if (verbose)
            {
                _statusMsg = $"Clipboard '{text}' is not a valid IP or IP:Port.";
                _statusColor = Color.red;
            }
            return false;
        }

        public override void LoadMenu()
        {
            MenuPanelUI.Instance.Clear();
            int num = 0;

            var header = new DuskersMenuItem("=== JOIN VIA DIRECT IP + PORT ===", KeyCode.None, null, num++);
            header.Disabled = true;
            header.OverridenColor = Color.cyan;
            MenuPanelUI.Instance.AddMenuItem(header);

            var targetItem = new DuskersMenuItem($"Target Host: [ {_targetIp} : {_targetPort} ]", KeyCode.None, null, num++);
            targetItem.Disabled = true;
            targetItem.OverridenColor = Color.green;
            MenuPanelUI.Instance.AddMenuItem(targetItem);

            // Connect button
            MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem($"[C]onnect to {_targetIp}:{_targetPort}", KeyCode.C, (m) =>
            {
                CoopNetworkManager.Instance.ConnectToHost(_targetIp, _targetPort);
                MenuPanelUI.Instance.PopMenu(2);
            }, num++));

            MenuPanelUI.Instance.AddMenuItem(null);
            num++;

            // IP Configuration
            var ipHeader = new DuskersMenuItem("--- Host IP Address ---", KeyCode.None, null, num++);
            ipHeader.Disabled = true;
            ipHeader.OverridenColor = Color.cyan;
            MenuPanelUI.Instance.AddMenuItem(ipHeader);

            string clip = (GUIUtility.systemCopyBuffer ?? string.Empty).Trim();
            string clipPreview = clip.Length > 20 ? clip.Substring(0, 20) + "..." : clip;
            string pasteLabel = string.IsNullOrEmpty(clip)
                ? "[P]aste IP from Clipboard"
                : $"[P]aste IP from Clipboard: '{clipPreview}'";

            MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem(pasteLabel, KeyCode.P, (m) =>
            {
                ParseAndApplyClipboard(clip, true);
                RefreshScreen();
            }, num++));

            string bufDisplay = string.IsNullOrEmpty(_ipBuffer) ? "<type digits 0-9 and dot>" : _ipBuffer + "_";
            var bufItem = new DuskersMenuItem($"Type IP: [ {bufDisplay} ]", KeyCode.None, null, num++);
            bufItem.Disabled = true;
            bufItem.OverridenColor = Color.yellow;
            MenuPanelUI.Instance.AddMenuItem(bufItem);

            if (!string.IsNullOrEmpty(_ipBuffer))
            {
                MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem($"[S]et IP: {_ipBuffer}", KeyCode.S, (m) =>
                {
                    ApplyIpBuffer();
                }, num++));

                MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem("[X] Clear IP Buffer", KeyCode.X, (m) =>
                {
                    _ipBuffer = "";
                    RefreshScreen();
                }, num++));
            }

            MenuPanelUI.Instance.AddMenuItem(null);
            num++;

            // Port Configuration
            var portHeader = new DuskersMenuItem($"--- Port Configuration (Current: {_targetPort}) ---", KeyCode.None, null, num++);
            portHeader.Disabled = true;
            portHeader.OverridenColor = Color.cyan;
            MenuPanelUI.Instance.AddMenuItem(portHeader);

            MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem($"P[o]rt Settings: {_targetPort}", KeyCode.O, (m) =>
            {
                new PortMenuScreen();
            }, num++));

            MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem("[+] Add 100 to Port", KeyCode.Plus, (m) =>
            {
                _targetPort = Math.Min(65535, _targetPort + 100);
                RefreshScreen();
            }, num++));

            MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem("[-] Subtract 100 from Port", KeyCode.Minus, (m) =>
            {
                _targetPort = Math.Max(1024, _targetPort - 100);
                RefreshScreen();
            }, num++));

            if (!string.IsNullOrEmpty(_statusMsg))
            {
                MenuPanelUI.Instance.AddMenuItem(null);
                num++;
                var statItem = new DuskersMenuItem(_statusMsg, KeyCode.None, null, num++);
                statItem.Disabled = true;
                statItem.OverridenColor = _statusColor;
                MenuPanelUI.Instance.AddMenuItem(statItem);
            }

            base.LoadMenu();
        }

        private void ApplyIpBuffer()
        {
            string candidate = _ipBuffer.Trim();
            if (IPAddress.TryParse(candidate, out _))
            {
                _targetIp = candidate;
                _statusMsg = $"Target IP updated to {_targetIp}!";
                _statusColor = Color.green;
            }
            else
            {
                _statusMsg = $"'{candidate}' is not a valid IPv4 address (e.g. 192.168.1.50).";
                _statusColor = Color.red;
            }
            RefreshScreen();
        }

        public override void Update()
        {
            base.Update();

            bool changed = false;

            // Keep port synced with network manager port if changed in PortMenuScreen
            var net = CoopNetworkManager.Instance;
            if (net != null && net.CurrentPort != _targetPort && _targetPort == CoopNetworkManager.DEFAULT_PORT)
            {
                _targetPort = net.CurrentPort;
                changed = true;
            }

            if (!string.IsNullOrEmpty(Input.inputString))
            {
                foreach (char c in Input.inputString)
                {
                    if (char.IsDigit(c) || c == '.')
                    {
                        if (_ipBuffer.Length < 16)
                        {
                            _ipBuffer += c;
                            changed = true;
                        }
                    }
                    else if (c == '\b' && _ipBuffer.Length > 0)
                    {
                        _ipBuffer = _ipBuffer.Substring(0, _ipBuffer.Length - 1);
                        changed = true;
                    }
                }
            }

            if (Input.GetKeyDown(KeyCode.Backspace) && _ipBuffer.Length > 0 && !changed)
            {
                _ipBuffer = _ipBuffer.Substring(0, _ipBuffer.Length - 1);
                changed = true;
            }

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                if (!string.IsNullOrEmpty(_ipBuffer))
                {
                    ApplyIpBuffer();
                    return;
                }
            }

            if (changed)
            {
                RefreshScreen();
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
