using System;
using DuskersCoopMod.Network;
using Steamworks;
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

            // 1. Steam Friends Playing Duskers (Direct 1-Click Join)
            if (SteamCoopManager.Instance != null && SteamCoopManager.Instance.IsSteamActive)
            {
                var friends = SteamCoopManager.Instance.GetFriendsPlayingDuskers();
                if (friends.Count > 0)
                {
                    var friendHeader = new DuskersMenuItem("--- STEAM FRIENDS PLAYING DUSKERS ---", KeyCode.None, null, num++);
                    friendHeader.Disabled = true;
                    friendHeader.OverridenColor = Color.green;
                    MenuPanelUI.Instance.AddMenuItem(friendHeader);

                    int idx = 1;
                    foreach (var f in friends)
                    {
                        var targetId = f.SteamId;
                        string fName = f.Name;
                        KeyCode key = (KeyCode)((int)KeyCode.Alpha1 + (idx - 1));
                        string label = $"[{idx}] Join {fName}'s Bridge (Steam P2P)";

                        MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem(label, key, (m) =>
                        {
                            SteamCoopManager.Instance.ConnectToHostBySteamId(targetId);
                        }, num++));

                        idx++;
                        if (idx > 5) break;
                    }

                    MenuPanelUI.Instance.AddMenuItem(null);
                    num++;
                }

                // Steam Overlay
                MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem("[O]pen Steam Friends Overlay (Shift+Tab)", KeyCode.O, (m) =>
                {
                    SteamCoopManager.Instance.OpenInviteOverlay();
                }, num++));

                MenuPanelUI.Instance.AddMenuItem(null);
                num++;
            }

            // 2. Quick Paste Option
            string clip = (GUIUtility.systemCopyBuffer ?? string.Empty).Trim();
            string clipPreview = clip.Length > 22 ? clip.Substring(0, 22) + "..." : clip;
            string pasteLabel = string.IsNullOrEmpty(clip)
                ? "[P] Fast Join from Clipboard (Steam ID / Code / IP)"
                : $"[P] Fast Join from Clipboard: '{clipPreview}'";

            MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem(pasteLabel, KeyCode.P, (m) =>
            {
                OnQuickPaste(clip);
            }, num++));

            var pasteTip = new DuskersMenuItem("    Accepts SteamID64, Session Code, or Direct IP:Port", KeyCode.None, null, num++);
            pasteTip.Disabled = true;
            pasteTip.OverridenColor = Color.yellow;
            MenuPanelUI.Instance.AddMenuItem(pasteTip);

            MenuPanelUI.Instance.AddMenuItem(null);
            num++;

            // 3. Option: Direct IP + Port (LAN / Radmin / Hamachi)
            MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem("[I] Join with Direct IP + Port (LAN / Radmin)", KeyCode.I, (m) =>
            {
                new JoinIpPortMenuScreen();
            }, num++));

            // 4. Option: Session Code
            MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem("[C] Join with Session Code", KeyCode.C, (m) =>
            {
                new JoinCodeMenuScreen();
            }, num++));

            base.LoadMenu();
        }

        private void OnQuickPaste(string clip)
        {
            if (string.IsNullOrEmpty(clip))
            {
                DialogUI.Instance.ShowDialog(
                    "Clipboard Empty",
                    "Your clipboard is empty.\r\nCopy a Steam ID, Session Code or IP address to your clipboard (Ctrl+C) and try again.",
                    ModalWindowType.OK,
                    null
                );
                return;
            }

            string cleaned = clip.Trim();

            // Check if clipboard is Steam ID
            if (cleaned.StartsWith("DSK-STEAM:", StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned.Substring("DSK-STEAM:".Length).Trim();
            }

            if (ulong.TryParse(cleaned, out ulong steamIdVal) && steamIdVal > 76561197960265728UL)
            {
                if (SteamCoopManager.Instance != null && SteamCoopManager.Instance.IsSteamActive)
                {
                    SteamCoopManager.Instance.ConnectToHostBySteamId(new CSteamID(steamIdVal));
                    return;
                }
                else
                {
                    DialogUI.Instance.ShowDialog(
                        "Steam Required",
                        "A Steam ID was detected, but Steam/GreenLuma is not currently running.\r\nPlease ensure Steam is active or use Direct IP.",
                        ModalWindowType.OK,
                        null
                    );
                    return;
                }
            }

            // Check Session Code
            string ip;
            int port;
            if (SessionCodeHelper.Decode(cleaned, out ip, out port))
            {
                CoopNetworkManager.Instance.ConnectToHost(ip, port);
                MenuPanelUI.Instance.Clear();
                MenuPanelUI.Instance.Reset();
                new CoopMenuScreen();
                return;
            }

            // Check Direct IP:Port (e.g. 26.12.34.56:7777 or 192.168.1.10:9600)
            if (cleaned.Contains(":"))
            {
                string[] parts = cleaned.Split(':');
                if (parts.Length == 2 && int.TryParse(parts[1].Trim(), out int parsedPort) && parsedPort >= 1024 && parsedPort <= 65535)
                {
                    CoopNetworkManager.Instance.ConnectToHost(parts[0].Trim(), parsedPort);
                    MenuPanelUI.Instance.Clear();
                    MenuPanelUI.Instance.Reset();
                    new CoopMenuScreen();
                    return;
                }
            }

            // If only IP is given, connect with default port
            System.Net.IPAddress testIp;
            if (System.Net.IPAddress.TryParse(cleaned, out testIp))
            {
                CoopNetworkManager.Instance.ConnectToHost(cleaned, CoopNetworkManager.DEFAULT_PORT);
                MenuPanelUI.Instance.Clear();
                MenuPanelUI.Instance.Reset();
                new CoopMenuScreen();
                return;
            }

            DialogUI.Instance.ShowDialog(
                "Unrecognized Format",
                $"Clipboard content:\r\n'{clip}'\r\n\r\nCould not automatically parse as Steam ID, Code, or IP.\r\nPlease select [I] for Direct IP or [C] for Code.",
                ModalWindowType.OK,
                null
            );
        }
    }
}
