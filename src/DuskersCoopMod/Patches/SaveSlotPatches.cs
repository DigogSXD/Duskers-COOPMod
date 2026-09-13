using System;
using System.IO;
using System.Reflection;
using DuskersCoopMod.Network;
using DuskersCoopMod.Save;
using HarmonyLib;
using UnityEngine;

namespace DuskersCoopMod.Patches
{
    [HarmonyPatch(typeof(GameFileHelper))]
    public static class SaveSlotPatches
    {
        public static string RedirectToCoopSlotIfNeeded(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;

            var net = CoopNetworkManager.Instance;
            bool isClient = (net != null && net.Role == NetworkRole.Client) || SaveSlotManager.IsUsingCoopRemoteSlot;
            if (!isClient) return path;

            string coopPath = SaveSlotManager.GetCoopSlotPath();
            if (path.StartsWith(coopPath, StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }

            string defaultBaseDoc = Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games"), "Duskers");
            if (path.StartsWith(defaultBaseDoc, StringComparison.OrdinalIgnoreCase))
            {
                string relative = path.Substring(defaultBaseDoc.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return Path.Combine(coopPath, relative);
            }

            return path;
        }

        [HarmonyPostfix]
        [HarmonyPatch("GetBaseGameFileLocation")]
        public static void GetBaseGameFileLocation_Postfix(ref string __result)
        {
            if (SaveSlotManager.IsUsingCoopRemoteSlot || (CoopNetworkManager.Instance != null && CoopNetworkManager.Instance.Role == NetworkRole.Client))
            {
                __result = SaveSlotManager.GetCoopSlotPath();
                return;
            }

            if (SaveSlotManager.CurrentSlot > 1)
            {
                __result = Path.Combine(__result, "Slot" + SaveSlotManager.CurrentSlot);
            }
        }
    }

    [HarmonyPatch(typeof(SettingsFile))]
    public static class SettingsFilePatches
    {
        [HarmonyPrefix]
        [HarmonyPatch("SaveSettingFile", new Type[] { typeof(string) })]
        public static void SaveSettingFile_Prefix(SettingsFile __instance, ref string filename)
        {
            string redirected = SaveSlotPatches.RedirectToCoopSlotIfNeeded(filename);
            if (redirected != filename)
            {
                filename = redirected;
                try
                {
                    var field = typeof(SettingsFile).GetField("sourceFile", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (field != null)
                    {
                        field.SetValue(__instance, redirected);
                    }
                }
                catch { }
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch("LoadSettingFile", new Type[] { typeof(string) })]
        public static void LoadSettingFile_Prefix(SettingsFile __instance, ref string filename)
        {
            string redirected = SaveSlotPatches.RedirectToCoopSlotIfNeeded(filename);
            if (redirected != filename)
            {
                filename = redirected;
                try
                {
                    var field = typeof(SettingsFile).GetField("sourceFile", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (field != null)
                    {
                        field.SetValue(__instance, redirected);
                    }
                }
                catch { }
            }
        }
    }

    [HarmonyPatch(typeof(RetryableFileWriter))]
    public static class RetryableFileWriterPatches
    {
        [HarmonyPrefix]
        [HarmonyPatch("ShowDialog")]
        public static bool ShowDialog_Prefix()
        {
            var net = CoopNetworkManager.Instance;
            bool isClient = (net != null && net.Role == NetworkRole.Client) || SaveSlotManager.IsUsingCoopRemoteSlot;

            if (isClient)
            {
                Debug.LogWarning("[DuskersCoopMod] Suppressed RetryableFileWriter error modal on client.");
                return false;
            }

            return true;
        }
    }
}
