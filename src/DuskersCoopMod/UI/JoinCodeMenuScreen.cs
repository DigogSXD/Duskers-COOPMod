using System;
using DuskersCoopMod.Network;
using UnityEngine;

namespace DuskersCoopMod.UI
{
    public class JoinCodeMenuScreen : MenuScreenClass
    {
        private string _activeCode = "";
        private string _inputBuffer = "";
        private string _statusMsg = "";
        private Color _statusColor = Color.green;

        public JoinCodeMenuScreen() : base(null)
        {
        }

        protected override void Initialize()
        {
            ActiveText = "Join via Session Code";
            IgnoreCancel = false;

            // Pre-fill if clipboard has a code
            string clip = (GUIUtility.systemCopyBuffer ?? string.Empty).Trim();
            if (clip.StartsWith("DSK-", StringComparison.OrdinalIgnoreCase))
            {
                _activeCode = clip.ToUpper();
                _inputBuffer = clip.ToUpper();
            }
        }

        public override void LoadMenu()
        {
            MenuPanelUI.Instance.Clear();
            int num = 0;

            var header = new DuskersMenuItem("=== JOIN VIA SESSION CODE ===", KeyCode.None, null, num++);
            header.Disabled = true;
            header.OverridenColor = Color.cyan;
            MenuPanelUI.Instance.AddMenuItem(header);

            string codeDisplay = string.IsNullOrEmpty(_activeCode) ? "<no code set>" : _activeCode;
            var codeItem = new DuskersMenuItem($"Active Code: [ {codeDisplay} ]", KeyCode.None, null, num++);
            codeItem.Disabled = true;
            codeItem.OverridenColor = string.IsNullOrEmpty(_activeCode) ? Color.yellow : Color.green;
            MenuPanelUI.Instance.AddMenuItem(codeItem);

            // Connect button if code is valid
            string targetIp = "";
            int targetPort = 0;
            bool isValid = !string.IsNullOrEmpty(_activeCode) && SessionCodeHelper.Decode(_activeCode, out targetIp, out targetPort);

            if (isValid)
            {
                string ipToUse = targetIp;
                int portToUse = targetPort;
                MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem($"[C]onnect to Host ({ipToUse}:{portToUse})", KeyCode.C, (m) =>
                {
                    CoopNetworkManager.Instance.ConnectToHost(ipToUse, portToUse);
                    MenuPanelUI.Instance.PopMenu(2);
                }, num++));
            }

            MenuPanelUI.Instance.AddMenuItem(null);
            num++;

            // Clipboard Paste
            string clip = (GUIUtility.systemCopyBuffer ?? string.Empty).Trim();
            string clipPreview = clip.Length > 18 ? clip.Substring(0, 18) + "..." : clip;
            string pasteLabel = string.IsNullOrEmpty(clip)
                ? "[P]aste Code from Clipboard"
                : $"[P]aste Code from Clipboard: '{clipPreview}'";

            MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem(pasteLabel, KeyCode.P, (m) =>
            {
                if (!string.IsNullOrEmpty(clip))
                {
                    _activeCode = clip.ToUpper();
                    _inputBuffer = clip.ToUpper();
                    _statusMsg = "Code pasted from clipboard.";
                    _statusColor = Color.green;
                }
                else
                {
                    _statusMsg = "Clipboard is empty.";
                    _statusColor = Color.red;
                }
                RefreshScreen();
            }, num++));

            // Typing buffer
            MenuPanelUI.Instance.AddMenuItem(null);
            num++;

            var typeHeader = new DuskersMenuItem("Type Code (0-9, A-F, dashes):", KeyCode.None, null, num++);
            typeHeader.Disabled = true;
            typeHeader.OverridenColor = Color.cyan;
            MenuPanelUI.Instance.AddMenuItem(typeHeader);

            string bufDisplay = string.IsNullOrEmpty(_inputBuffer) ? "<type on keyboard>" : _inputBuffer + "_";
            var bufItem = new DuskersMenuItem($"Buffer: [ {bufDisplay} ]", KeyCode.None, null, num++);
            bufItem.Disabled = true;
            bufItem.OverridenColor = Color.yellow;
            MenuPanelUI.Instance.AddMenuItem(bufItem);

            if (!string.IsNullOrEmpty(_inputBuffer))
            {
                MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem($"[A]pply Code: {_inputBuffer}", KeyCode.A, (m) =>
                {
                    _activeCode = _inputBuffer.Trim().ToUpper();
                    RefreshScreen();
                }, num++));

                MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem("[X] Clear Buffer", KeyCode.X, (m) =>
                {
                    _inputBuffer = "";
                    RefreshScreen();
                }, num++));
            }

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

        public override void Update()
        {
            base.Update();

            bool changed = false;

            if (!string.IsNullOrEmpty(Input.inputString))
            {
                foreach (char c in Input.inputString)
                {
                    char up = char.ToUpper(c);
                    if (char.IsDigit(up) || (up >= 'A' && up <= 'F') || up == '-' || up == 'K' || up == 'S' || up == 'D')
                    {
                        if (_inputBuffer.Length < 18)
                        {
                            _inputBuffer += up;
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
                    _activeCode = _inputBuffer.Trim().ToUpper();
                    changed = true;
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
