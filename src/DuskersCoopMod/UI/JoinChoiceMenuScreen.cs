using System;
using DuskersCoopMod.Network;
using UnityEngine;

namespace DuskersCoopMod.UI
{
    public class JoinChoiceMenuScreen : MenuScreenClass
    {
        public JoinChoiceMenuScreen() : base(null)
        {
        }

        protected override void Initialize()
        {
            ActiveText = "Join Session";
            IgnoreCancel = false;
        }

        public override void LoadMenu()
        {
            MenuPanelUI.Instance.Clear();
            int num = 0;

            var header = new DuskersMenuItem("=== JOIN SESSION: SELECT METHOD ===", KeyCode.None, null, num++);
            header.Disabled = true;
            header.OverridenColor = Color.cyan;
            MenuPanelUI.Instance.AddMenuItem(header);

            // Option 1: Session Code
            MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem("[C] Join with Session Code", KeyCode.C, (m) =>
            {
                new JoinCodeMenuScreen();
            }, num++));

            var codeDesc = new DuskersMenuItem("    Fast join using 12-char code (e.g. DSK-1AE0-3323-6C1E)", KeyCode.None, null, num++);
            codeDesc.Disabled = true;
            codeDesc.OverridenColor = Color.yellow;
            MenuPanelUI.Instance.AddMenuItem(codeDesc);

            MenuPanelUI.Instance.AddMenuItem(null);
            num++;

            // Option 2: Direct IP + Port
            MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem("[I] Join with Direct IP + Port", KeyCode.I, (m) =>
            {
                new JoinIpPortMenuScreen();
            }, num++));

            var ipDesc = new DuskersMenuItem("    Manually enter Host IP address and TCP Port (e.g. Radmin/Hamachi/LAN)", KeyCode.None, null, num++);
            ipDesc.Disabled = true;
            ipDesc.OverridenColor = Color.yellow;
            MenuPanelUI.Instance.AddMenuItem(ipDesc);

            MenuPanelUI.Instance.AddMenuItem(null);
            num++;

            // Quick Paste Option
            string clip = (GUIUtility.systemCopyBuffer ?? string.Empty).Trim();
            string clipPreview = clip.Length > 20 ? clip.Substring(0, 20) + "..." : clip;
            string pasteLabel = string.IsNullOrEmpty(clip)
                ? "[P] Fast Join from Clipboard"
                : $"[P] Fast Join from Clipboard: '{clipPreview}'";

            MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem(pasteLabel, KeyCode.P, (m) =>
            {
                OnQuickPaste(clip);
            }, num++));

            base.LoadMenu();
        }

        private void OnQuickPaste(string clip)
        {
            if (string.IsNullOrEmpty(clip))
            {
                DialogUI.Instance.ShowDialog(
                    "Clipboard Empty",
                    "Your clipboard is empty.\r\nCopy a Session Code or IP address to your clipboard (Ctrl+C) and try again.",
                    ModalWindowType.OK,
                    null
                );
                return;
            }

            string ip;
            int port;
            if (SessionCodeHelper.Decode(clip, out ip, out port))
            {
                CoopNetworkManager.Instance.ConnectToHost(ip, port);
                MenuPanelUI.Instance.Clear();
                MenuPanelUI.Instance.Reset();
                new CoopMenuScreen();
            }
            else
            {
                DialogUI.Instance.ShowDialog(
                    "Unrecognized Format",
                    $"Clipboard content:\r\n'{clip}'\r\n\r\nCould not automatically parse as Code or IP.\r\nPlease select [C] for Code or [I] for Direct IP + Port.",
                    ModalWindowType.OK,
                    null
                );
            }
        }
    }
}
