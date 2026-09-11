using System;
using System.Net;
using System.Net.Sockets;

namespace DuskersCoopMod.Network
{
    public static class SessionCodeHelper
    {
        public static string Encode(string ipStr, int port)
        {
            try
            {
                IPAddress ip;
                if (!IPAddress.TryParse(ipStr, out ip))
                {
                    return ipStr;
                }

                byte[] ipBytes = ip.GetAddressBytes();
                byte[] portBytes = BitConverter.GetBytes((ushort)port);

                byte[] combined = new byte[6];
                Array.Copy(ipBytes, 0, combined, 0, 4);
                Array.Copy(portBytes, 0, combined, 4, 2);

                string hex = BitConverter.ToString(combined).Replace("-", "").ToUpper();
                return $"DSK-{hex.Substring(0, 4)}-{hex.Substring(4, 4)}-{hex.Substring(8, 4)}";
            }
            catch
            {
                return ipStr;
            }
        }

        public static bool Decode(string code, out string ipStr, out int port)
        {
            ipStr = null;
            port = CoopNetworkManager.DEFAULT_PORT;

            if (string.IsNullOrEmpty(code)) return false;

            string clean = code.Trim().ToUpper().Replace("DSK-", "").Replace("-", "").Replace(" ", "");

            if (clean.Length == 12)
            {
                try
                {
                    byte[] bytes = new byte[6];
                    for (int i = 0; i < 6; i++)
                    {
                        bytes[i] = Convert.ToByte(clean.Substring(i * 2, 2), 16);
                    }
                    ipStr = $"{bytes[0]}.{bytes[1]}.{bytes[2]}.{bytes[3]}";
                    port = BitConverter.ToUInt16(bytes, 4);
                    return true;
                }
                catch
                {
                    // Fallthrough to standard IP parsing
                }
            }

            // Also support direct IP or IP:Port
            if (code.Contains("."))
            {
                string[] parts = code.Trim().Split(':');
                ipStr = parts[0].Trim();
                if (parts.Length > 1 && int.TryParse(parts[1], out int parsedPort))
                {
                    port = parsedPort;
                }
                return true;
            }

            return false;
        }

        public static string GetPreferredLocalIp()
        {
            try
            {
                string hostName = Dns.GetHostName();
                IPAddress[] addresses = Dns.GetHostAddresses(hostName);

                string bestIp = null;

                foreach (IPAddress addr in addresses)
                {
                    if (addr.AddressFamily == AddressFamily.InterNetwork)
                    {
                        string ipStr = addr.ToString();
                        if (ipStr.StartsWith("127.")) continue;

                        // Prioritize common local/VPN ranges (LAN 192.168, Hamachi 25., Radmin 26., Tailscale 100., LAN 10.)
                        if (ipStr.StartsWith("192.168.") || ipStr.StartsWith("25.") || ipStr.StartsWith("26.") || ipStr.StartsWith("100."))
                        {
                            return ipStr;
                        }

                        if (bestIp == null)
                        {
                            bestIp = ipStr;
                        }
                    }
                }

                return bestIp ?? "127.0.0.1";
            }
            catch
            {
                return "127.0.0.1";
            }
        }
    }
}
