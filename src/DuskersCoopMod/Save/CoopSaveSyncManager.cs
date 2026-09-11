using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using DuskersCoopMod.Network;
using UnityEngine;

namespace DuskersCoopMod.Save
{
    [Serializable]
    public class SaveFileItem
    {
        public string path;
        public string dataBase64;
    }

    [Serializable]
    public class SavePackageData
    {
        public List<SaveFileItem> items = new List<SaveFileItem>();
    }

    public static class CoopSaveSyncManager
    {
        public static string PackageActiveSave(int slot)
        {
            try
            {
                string sourceDir = SaveSlotManager.GetSlotPath(slot);
                if (!Directory.Exists(sourceDir)) return null;

                var package = new SavePackageData();

                // Add gamesave.txt if exists
                string gameSaveFile = Path.Combine(sourceDir, "gamesave.txt");
                if (File.Exists(gameSaveFile))
                {
                    package.items.Add(new SaveFileItem
                    {
                        path = "gamesave.txt",
                        dataBase64 = Convert.ToBase64String(File.ReadAllBytes(gameSaveFile))
                    });
                }

                // Add data folder recursively
                string dataDir = Path.Combine(sourceDir, "data");
                if (Directory.Exists(dataDir))
                {
                    string[] allFiles = Directory.GetFiles(dataDir, "*.*", SearchOption.AllDirectories);
                    foreach (string filePath in allFiles)
                    {
                        string relative = filePath.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        FileInfo fi = new FileInfo(filePath);
                        if (fi.Length > 2000000) continue; // skip files > 2MB

                        package.items.Add(new SaveFileItem
                        {
                            path = relative.Replace('\\', '/'),
                            dataBase64 = Convert.ToBase64String(File.ReadAllBytes(filePath))
                        });
                    }
                }

                string json = JsonUtility.ToJson(package);
                byte[] rawBytes = Encoding.UTF8.GetBytes(json);

                using (MemoryStream ms = new MemoryStream())
                {
                    using (GZipStream gzip = new GZipStream(ms, CompressionMode.Compress, true))
                    {
                        gzip.Write(rawBytes, 0, rawBytes.Length);
                    }
                    return Convert.ToBase64String(ms.ToArray());
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DuskersCoopMod] Error packaging save for slot {slot}: {ex}");
                return null;
            }
        }

        public static bool ApplyReceivedSave(string compressedBase64)
        {
            try
            {
                byte[] compressedBytes = Convert.FromBase64String(compressedBase64);
                string json;

                using (MemoryStream ms = new MemoryStream(compressedBytes))
                using (GZipStream gzip = new GZipStream(ms, CompressionMode.Decompress))
                using (MemoryStream outMs = new MemoryStream())
                {
                    byte[] buffer = new byte[4096];
                    int read;
                    while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        outMs.Write(buffer, 0, read);
                    }
                    json = Encoding.UTF8.GetString(outMs.ToArray());
                }

                SavePackageData package = JsonUtility.FromJson<SavePackageData>(json);
                if (package == null || package.items == null) return false;

                string targetDir = SaveSlotManager.GetCoopSlotPath();
                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                foreach (var item in package.items)
                {
                    string destPath = Path.Combine(targetDir, item.path.Replace('/', Path.DirectorySeparatorChar));
                    string destDir = Path.GetDirectoryName(destPath);
                    if (!Directory.Exists(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }
                    byte[] data = Convert.FromBase64String(item.dataBase64);
                    File.WriteAllBytes(destPath, data);
                }

                SaveSlotManager.IsUsingCoopRemoteSlot = true;

                // Re-initialize Duskers save system in memory
                GameFileHelper.EnsureGameFileDirectoriesExist();
                GameSaveFile.ReInitSetting();
                UniverseSaveFile.ReInitSetting();

                Debug.Log("[DuskersCoopMod] Successfully applied Host's synchronized save to SlotCoop!");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DuskersCoopMod] Error applying received save: {ex}");
                return false;
            }
        }

        public static void SyncToAllClients()
        {
            var net = CoopNetworkManager.Instance;
            if (net == null || net.Role != NetworkRole.Host) return;

            string base64 = PackageActiveSave(SaveSlotManager.CurrentSlot);
            if (string.IsNullOrEmpty(base64)) return;

            string packet = PacketWrapper.Create("SAVE_SYNC", "Host", new SaveSyncData
            {
                compressedBase64 = base64
            });

            net.BroadcastPacket(packet);
            Debug.Log("[DuskersCoopMod] Host save state broadcasted to all connected operators.");
        }
    }
}
