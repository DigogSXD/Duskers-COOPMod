using System;
using System.IO;
using UnityEngine;

namespace DuskersCoopMod.Save
{
    public static class SaveSlotManager
    {
        public static int CurrentSlot { get; private set; } = 1;
        public static bool IsUsingCoopRemoteSlot { get; set; } = false;
        private static string _configPath;

        public static string GetCoopSlotPath()
        {
            string baseDoc = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games");
            return Path.Combine(Path.Combine(baseDoc, "Duskers"), "SlotCoop");
        }

        static SaveSlotManager()
        {
            try
            {
                string baseDoc = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games");
                string duskersDoc = Path.Combine(baseDoc, "Duskers");
                _configPath = Path.Combine(duskersDoc, "active_slot.txt");

                if (File.Exists(_configPath))
                {
                    string text = File.ReadAllText(_configPath).Trim();
                    if (int.TryParse(text, out int slot) && slot >= 1 && slot <= 3)
                    {
                        CurrentSlot = slot;
                    }
                }
            }
            catch
            {
                CurrentSlot = 1;
            }
        }

        public static void SwitchSlot(int slot)
        {
            if (slot < 1 || slot > 3) return;

            CurrentSlot = slot;
            try
            {
                if (!string.IsNullOrEmpty(_configPath))
                {
                    File.WriteAllText(_configPath, slot.ToString());
                }
            }
            catch { }

            // Ensure directories for the newly selected slot exist
            GameFileHelper.EnsureGameFileDirectoriesExist();

            // Re-initialize save files in memory for the new slot
            try
            {
                GameSaveFile.ReInitSetting();
                UniverseSaveFile.ReInitSetting();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DuskersCoopMod] Error re-initializing save files for slot {slot}: {ex}");
            }
        }

        public static string GetSlotPath(int slot)
        {
            string baseDoc = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games");
            string duskersDoc = Path.Combine(baseDoc, "Duskers");

            if (slot == 1)
            {
                return duskersDoc;
            }
            return Path.Combine(duskersDoc, $"Slot{slot}");
        }

        public static string GetSlotSummary(int slot)
        {
            string slotDir = GetSlotPath(slot);
            string saveFile = Path.Combine(slotDir, "gamesave.txt");

            if (!File.Exists(saveFile))
            {
                return "[Empty Slot]";
            }

            try
            {
                string[] lines = File.ReadAllLines(saveFile);
                int plays = 0;
                string ver = "1.205";

                foreach (string line in lines)
                {
                    if (line.StartsWith("PLAYS="))
                    {
                        int.TryParse(line.Substring(6), out plays);
                    }
                    else if (line.StartsWith("GAME_VER="))
                    {
                        ver = line.Substring(9);
                    }
                }

                return $"Saved Data (Plays: {plays})";
            }
            catch
            {
                return "Saved Data";
            }
        }

        public static void DeleteSlot(int slot)
        {
            string slotDir = GetSlotPath(slot);

            try
            {
                if (slot == 1)
                {
                    // For slot 1, delete save files and udata to reset, preserving config
                    string saveFile = Path.Combine(slotDir, "gamesave.txt");
                    if (File.Exists(saveFile)) File.Delete(saveFile);

                    string udata = Path.Combine(slotDir, Path.Combine("data", "udata"));
                    if (Directory.Exists(udata)) Directory.Delete(udata, true);
                }
                else
                {
                    // For slots 2 and 3, remove the entire slot folder
                    if (Directory.Exists(slotDir))
                    {
                        Directory.Delete(slotDir, true);
                    }
                }

                // If deleting active slot, re-init blank state
                if (CurrentSlot == slot)
                {
                    GameFileHelper.EnsureGameFileDirectoriesExist();
                    GameSaveFile.ReInitSetting();
                    UniverseSaveFile.ReInitSetting();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DuskersCoopMod] Error deleting slot {slot}: {ex}");
            }
        }
    }
}
