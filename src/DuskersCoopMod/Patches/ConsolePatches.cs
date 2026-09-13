using System;
using DuskersCoopMod.Network;
using HarmonyLib;
using UnityEngine;

namespace DuskersCoopMod.Patches
{
    [HarmonyPatch(typeof(ConsoleWindow3))]
    public static class ConsolePatches
    {
        private static string _lastTargetIp = null;
        private static int _lastTargetPort = CoopNetworkManager.DEFAULT_PORT;

        [HarmonyPrefix]
        [HarmonyPatch("CheckForBackspaceOrDelete")]
        public static void CheckForBackspaceOrDelete_Prefix(ConsoleWindow3 __instance)
        {
            try
            {
                var tr = Traverse.Create(__instance);
                string cmd = tr.Field("_commandText").GetValue<string>();
                int pos = tr.Field("_cursorPosition").GetValue<int>();
                int len = cmd != null ? cmd.Length : 0;
                if (pos > len)
                {
                    tr.Field("_cursorPosition").SetValue(len);
                }
                else if (pos < 0)
                {
                    tr.Field("_cursorPosition").SetValue(0);
                }
            }
            catch { }
        }

        private static string _lastExecutedHostCommand = null;
        public static bool IsExecutingRemoteCommand = false;

        [HarmonyPrefix]
        [HarmonyPatch("AttemptExecuteCommand")]
        public static bool AttemptExecuteCommand_Prefix(ConsoleWindow3 __instance)
        {
            if (IsExecutingRemoteCommand)
            {
                return true;
            }

            var traverse = Traverse.Create(__instance);
            string commandText = traverse.Field("_commandText").GetValue<string>();

            if (string.IsNullOrEmpty(commandText))
            {
                return true;
            }

            string trimmed = commandText.Trim();

            // Intercept COOP mod commands
            if (trimmed.StartsWith("coop", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("net", StringComparison.OrdinalIgnoreCase))
            {
                HandleCoopCommand(trimmed);
                traverse.Field("_commandText").SetValue(string.Empty);
                traverse.Method("RefreshCurrentLine").GetValue();
                return false;
            }

            var net = CoopNetworkManager.Instance;

            // If we are connected as CLIENT, forward command to HOST and don't execute locally yet
            // Host will execute and broadcast EXECUTE_COMMAND so all operators execute in sync
            if (net != null && net.Role == NetworkRole.Client && net.IsConnected)
            {
                // Echo on client console
                traverse.Method("AddTextToConsole", new object[] { new ConsoleMessage("[YOU] > " + trimmed, ConsoleMessageType.Info) }).GetValue();
                traverse.Field("_commandText").SetValue(string.Empty);
                traverse.Method("RefreshCurrentLine").GetValue();

                // Send to Host
                net.SendCommand(trimmed);
                return false;
            }

            // If we are HOST, record command so Postfix can broadcast to clients
            if (net != null && net.Role == NetworkRole.Host && net.ConnectedCount > 0)
            {
                _lastExecutedHostCommand = trimmed;
            }

            return true;
        }

        [HarmonyPostfix]
        [HarmonyPatch("AttemptExecuteCommand")]
        public static void AttemptExecuteCommand_Postfix()
        {
            if (IsExecutingRemoteCommand) return;
            var net = CoopNetworkManager.Instance;
            if (net != null && net.Role == NetworkRole.Host && net.ConnectedCount > 0 && !string.IsNullOrEmpty(_lastExecutedHostCommand))
            {
                net.BroadcastPacket(PacketWrapper.Create("EXECUTE_COMMAND", "Host", new CommandData { command = _lastExecutedHostCommand }));
                _lastExecutedHostCommand = null;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("AddTextToConsole", new Type[] { typeof(ConsoleMessage) })]
        public static void AddTextToConsole_Postfix(ConsoleMessage msgObject)
        {
            if (msgObject == null) return;

            var net = CoopNetworkManager.Instance;
            if (net == null || net.IsApplyingRemoteConsoleMessage) return;

            // If we are Host, broadcast console messages to all connected operators
            if (net.Role == NetworkRole.Host && net.ConnectedCount > 0)
            {
                net.SendConsoleText(msgObject.Message, msgObject.Type, msgObject.Format);
            }
        }

        private static void HandleCoopCommand(string fullCommand)
        {
            var net = CoopNetworkManager.Instance;
            if (net == null) return;

            string[] parts = fullCommand.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                ShowHelp();
                return;
            }

            string subCmd = parts[1].ToLower();

            switch (subCmd)
            {
                case "host":
                    int hostPort = net.CurrentPort;
                    if (parts.Length >= 3 && int.TryParse(parts[2], out int parsedPort))
                    {
                        hostPort = parsedPort;
                    }
                    net.StartHost(hostPort);
                    break;

                case "port":
                    if (parts.Length < 3)
                    {
                        net.PrintToLocalConsole($"[COOP] Current port: {net.CurrentPort}. Usage: coop port <1024-65535>", ConsoleMessageType.Info);
                        return;
                    }
                    if (int.TryParse(parts[2], out int newPort))
                    {
                        net.SetPort(newPort);
                    }
                    else
                    {
                        net.PrintToLocalConsole("[COOP] Invalid port. Usage: coop port <number>", ConsoleMessageType.Error);
                    }
                    break;

                case "connect":
                case "join":
                    if (parts.Length < 3)
                    {
                        net.PrintToLocalConsole("[COOP] Usage: coop connect <code or ip> [port]", ConsoleMessageType.Error);
                        return;
                    }
                    string rawTarget = parts[2];
                    string targetIp;
                    int targetPort;

                    if (SessionCodeHelper.Decode(rawTarget, out targetIp, out targetPort))
                    {
                        if (parts.Length >= 4 && int.TryParse(parts[3], out int overridePort))
                        {
                            targetPort = overridePort;
                        }
                    }
                    else
                    {
                        targetIp = rawTarget;
                        targetPort = net.CurrentPort;
                        if (parts.Length >= 4 && int.TryParse(parts[3], out int overridePort))
                        {
                            targetPort = overridePort;
                        }
                    }

                    _lastTargetIp = targetIp;
                    _lastTargetPort = targetPort;
                    net.ConnectToHost(targetIp, targetPort);
                    break;

                case "reconnect":
                    if (net != null)
                    {
                        net.Reconnect();
                    }
                    break;

                case "disconnect":
                case "stop":
                    net.Disconnect();
                    break;

                case "status":
                    string info = net.Role == NetworkRole.Host
                        ? $"Role: Host | Port: {net.CurrentPort} | Operators: {net.ConnectedCount}"
                        : $"Role: Client | Connected: {net.IsConnected} | Host: {net.RemoteEndpointInfo}";
                    net.PrintToLocalConsole($"[COOP STATUS] {info}", ConsoleMessageType.SpecialInfo);
                    break;

                case "help":
                default:
                    ShowHelp();
                    break;
            }
        }

        private static void ShowHelp()
        {
            var net = CoopNetworkManager.Instance;
            if (net == null) return;

            net.PrintToLocalConsole("=== DUSKERS COOPERATIVE TERMINAL ===", ConsoleMessageType.SpecialInfo, ConsoleMessageFormat.HeaderFont);
            net.PrintToLocalConsole($"coop host [port]         - Start hosting server (current: {net.CurrentPort})", ConsoleMessageType.Info);
            net.PrintToLocalConsole("coop port <number>       - Change or view server port (1024-65535)", ConsoleMessageType.Info);
            net.PrintToLocalConsole("coop connect <code/ip>   - Connect to host (accepts DSK-XXXX code or IP)", ConsoleMessageType.Info);
            net.PrintToLocalConsole("coop reconnect           - Reconnect to previous host", ConsoleMessageType.Info);
            net.PrintToLocalConsole("coop disconnect          - Disconnect current session", ConsoleMessageType.Info);
            net.PrintToLocalConsole("coop status              - Show connection status & operators", ConsoleMessageType.Info);
            net.PrintToLocalConsole("All connected operators share full fleet control in real-time.", ConsoleMessageType.Benefit);
        }
    }
}
