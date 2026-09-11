using System;
using DuskersCoopMod.Network;
using UnityEngine;

namespace DuskersCoopMod.UI
{
    public class PortMenuScreen : MenuScreenClass
    {
        private string _inputBuffer = "";
        private string _statusFeedback = "";
        private Color _statusColor = Color.green;

        public PortMenuScreen() : base(null)
        {
        }

        protected override void Initialize()
        {
            ActiveText = "Configure Server Port";
            IgnoreCancel = false;
        }

        public override void LoadMenu()
        {
            MenuPanelUI.Instance.Clear();
            int num = 0;

            var net = CoopNetworkManager.Instance;
            int currentPort = net != null ? net.CurrentPort : CoopNetworkManager.DEFAULT_PORT;

            var header = new DuskersMenuItem("=== CONFIGURE SERVER PORT ===", KeyCode.None, null, num++);
            header.Disabled = true;
            header.OverridenColor = Color.cyan;
            MenuPanelUI.Instance.AddMenuItem(header);

            var activePortItem = new DuskersMenuItem($"Active Server Port: {currentPort}", KeyCode.None, null, num++);
            activePortItem.Disabled = true;
            activePortItem.OverridenColor = Color.green;
            MenuPanelUI.Instance.AddMenuItem(activePortItem);

            MenuPanelUI.Instance.AddMenuItem(null);
            num++;

            // Input buffer display
            string bufferDisplay = string.IsNullOrEmpty(_inputBuffer) ? "<type digits 0-9>" : _inputBuffer + "_";
            var bufferItem = new DuskersMenuItem($"Input Buffer: [ {bufferDisplay} ]", KeyCode.None, null, num++);
            bufferItem.Disabled = true;
            bufferItem.OverridenColor = Color.yellow;
            MenuPanelUI.Instance.AddMenuItem(bufferItem);

            // Apply button
            if (!string.IsNullOrEmpty(_inputBuffer))
            {
                MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem($"[A]pply Port: {_inputBuffer} (or press Enter)", KeyCode.A, (m) =>
                {
                    ApplyBuffer();
                }, num++));

                MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem("[C]lear Input", KeyCode.C, (m) =>
                {
                    _inputBuffer = "";
                    _statusFeedback = "";
                    RefreshScreen();
                }, num++));
            }

            // Clipboard Paste
            string clip = (GUIUtility.systemCopyBuffer ?? string.Empty).Trim();
            string clipPreview = clip.Length > 8 ? clip.Substring(0, 8) + "..." : clip;
            string pasteLabel = string.IsNullOrEmpty(clip)
                ? "[P]aste from Clipboard"
                : $"[P]aste from Clipboard ({clipPreview})";

            MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem(pasteLabel, KeyCode.P, (m) =>
            {
                if (int.TryParse(clip, out int parsed) && parsed >= 1024 && parsed <= 65535)
                {
                    net?.SetPort(parsed);
                    _inputBuffer = "";
                    _statusFeedback = $"Port set to {parsed} from clipboard!";
                    _statusColor = Color.green;
                }
                else
                {
                    _statusFeedback = $"Clipboard '{clipPreview}' is not a valid port (1024-65535).";
                    _statusColor = Color.red;
                }
                RefreshScreen();
            }, num++));

            MenuPanelUI.Instance.AddMenuItem(null);
            num++;

            // Presets
            var presetHeader = new DuskersMenuItem("Quick Presets:", KeyCode.None, null, num++);
            presetHeader.Disabled = true;
            presetHeader.OverridenColor = Color.cyan;
            MenuPanelUI.Instance.AddMenuItem(presetHeader);

            MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem("[D]efault Port (7777)", KeyCode.D, (m) =>
            {
                net?.SetPort(7777);
                _inputBuffer = "";
                _statusFeedback = "Port set to 7777 (Default).";
                _statusColor = Color.green;
                RefreshScreen();
            }, num++));

            MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem("[S]team Standard Port (27015)", KeyCode.S, (m) =>
            {
                net?.SetPort(27015);
                _inputBuffer = "";
                _statusFeedback = "Port set to 27015 (Steam Standard).";
                _statusColor = Color.green;
                RefreshScreen();
            }, num++));

            MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem("[+] Add 100 to Port", KeyCode.Plus, (m) =>
            {
                int newP = Math.Min(65535, currentPort + 100);
                net?.SetPort(newP);
                _inputBuffer = "";
                RefreshScreen();
            }, num++));

            MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem("[-] Subtract 100 from Port", KeyCode.Minus, (m) =>
            {
                int newP = Math.Max(1024, currentPort - 100);
                net?.SetPort(newP);
                _inputBuffer = "";
                RefreshScreen();
            }, num++));

            if (!string.IsNullOrEmpty(_statusFeedback))
            {
                MenuPanelUI.Instance.AddMenuItem(null);
                num++;
                var feedItem = new DuskersMenuItem(_statusFeedback, KeyCode.None, null, num++);
                feedItem.Disabled = true;
                feedItem.OverridenColor = _statusColor;
                MenuPanelUI.Instance.AddMenuItem(feedItem);
            }

            base.LoadMenu();
        }

        private void ApplyBuffer()
        {
            var net = CoopNetworkManager.Instance;
            if (int.TryParse(_inputBuffer, out int p))
            {
                if (p >= 1024 && p <= 65535)
                {
                    net?.SetPort(p);
                    _statusFeedback = $"Port successfully updated to {p}!";
                    _statusColor = Color.green;
                    _inputBuffer = "";
                }
                else
                {
                    _statusFeedback = $"Port {p} is out of range! Must be between 1024 and 65535.";
                    _statusColor = Color.red;
                }
            }
            else
            {
                _statusFeedback = "Invalid numeric port.";
                _statusColor = Color.red;
            }
            RefreshScreen();
        }

        public override void Update()
        {
            base.Update();

            bool changed = false;

            // Handle keyboard input for custom typing
            if (!string.IsNullOrEmpty(Input.inputString))
            {
                foreach (char c in Input.inputString)
                {
                    if (char.IsDigit(c))
                    {
                        if (_inputBuffer.Length < 5)
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

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                if (!string.IsNullOrEmpty(_inputBuffer))
                {
                    ApplyBuffer();
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
