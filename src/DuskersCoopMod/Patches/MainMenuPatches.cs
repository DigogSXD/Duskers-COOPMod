using System;
using System.Collections.Generic;
using DuskersCoopMod.Network;
using DuskersCoopMod.Save;
using DuskersCoopMod.UI;
using HarmonyLib;
using UnityEngine;

namespace DuskersCoopMod.Patches
{
    [HarmonyPatch(typeof(MainMenu))]
    public static class MainMenuPatches
    {
        [HarmonyPrefix]
        [HarmonyPatch("LoadMenu")]
        public static bool LoadMenu_Prefix(MainMenu __instance)
        {
            try
            {
                MainMenu.Instance = __instance;
                var traverse = Traverse.Create(__instance);

                bool delayLoadMenu = traverse.Field("delayLoadMenu").GetValue<bool>();
                if (delayLoadMenu)
                {
                    return true;
                }

                int num = 0;

                // 1. [P]lay Game
                MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem("[P]lay Game", KeyCode.P, (item) =>
                {
                    traverse.Method("MenuPlayGame", new object[] { item }).GetValue();
                }, num++));

                // 2. [M]ultiplayer
                var mpItem = new DuskersMenuItem($"[M]ultiplayer (v{CoopNetworkManager.MOD_VERSION})", KeyCode.M, (item) =>
                {
                    new CoopMenuScreen();
                }, num++);
                mpItem.OverridenColor = Color.cyan;
                MenuPanelUI.Instance.AddMenuItem(mpItem);

                // 3. Sa[v]e Slots
                var saveSlotItem = new DuskersMenuItem($"Sa[v]e Slots (Slot {SaveSlotManager.CurrentSlot})", KeyCode.V, (item) =>
                {
                    new SaveSlotMenuScreen();
                }, num++);
                saveSlotItem.OverridenColor = Color.yellow;
                MenuPanelUI.Instance.AddMenuItem(saveSlotItem);

                // 4. Challenge (if Steam initialized)
                bool steamInit = false;
                try
                {
                    Type steamMgrType = AccessTools.TypeByName("SteamManager");
                    if (steamMgrType != null)
                    {
                        steamInit = Traverse.Create(steamMgrType).Property("Initialized").GetValue<bool>();
                    }
                }
                catch { }

                if (steamInit)
                {
                    DuskersMenuItem challengeItem = new DuskersMenuItem("Ch[a]llenge", KeyCode.A, (item) =>
                    {
                        traverse.Method("MenuPlayChallenge", new object[] { item }).GetValue();
                    }, num++);
                    traverse.Field("challengeMenuItem").SetValue(MenuPanelUI.Instance.AddMenuItem(challengeItem));
                    traverse.Method("RefreshChallengeItem").GetValue();
                    MenuPanelUI.Instance.AddMenuItem(null);
                    num++;
                }

                // 5. Tutorial, Options, Help, Stats
                MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem("[D]rone Operator Training", KeyCode.D, (item) =>
                {
                    traverse.Method("MenuPlayTutorial", new object[] { item }).GetValue();
                }, num++));

                MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem("[O]ptions", KeyCode.O, (item) =>
                {
                    traverse.Method("MenuOptions", new object[] { item }).GetValue();
                }, num++));

                MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem("[H]elp Manual", KeyCode.H, (item) =>
                {
                    traverse.Method("ShowHelp", new object[] { item }).GetValue();
                }, num++));

                MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem("[S]tats", KeyCode.S, (item) =>
                {
                    traverse.Method("ShowStats", new object[] { item }).GetValue();
                }, num++));

                MenuPanelUI.Instance.AddMenuItem(null);
                num++;

                // 6. Forums, Misfits Attic (changed to KeyCode.T)
                MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem("[F]orums (Steam)", KeyCode.F, (item) =>
                {
                    traverse.Method("MenuForums", new object[] { item }).GetValue();
                }, num++));

                MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem("Misfits A[t]tic", KeyCode.T, (item) =>
                {
                    traverse.Method("MenuMisfitsAttic", new object[] { item }).GetValue();
                }, num++));

                // Scavenger feedback if applicable
                if (GameSaveFile.Get("SCAVENGER", false) && !GameSaveFile.Get("SCAVENGER_SUBMIT", false))
                {
                    DuskersMenuItem scav = new DuskersMenuItem("S[u]bmit Scavenger Hunt Feedback", KeyCode.U, (item) =>
                    {
                        traverse.Method("MenuSubmitWin", new object[] { item }).GetValue();
                    }, num++);
                    scav.OverridenColor = Color.yellow;
                    MenuPanelUI.Instance.AddMenuItem(scav);
                }

                MenuPanelUI.Instance.AddMenuItem(null);
                num++;

                // 7. Credits, Exit
                MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem("C[r]edits", KeyCode.R, (item) =>
                {
                    traverse.Method("MenuShowCredits", new object[] { item }).GetValue();
                }, num++));

                MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem("[E]xit", KeyCode.E, (item) =>
                {
                    traverse.Method("MenuExitGame", new object[] { item }).GetValue();
                }, num++));

                // Cheat items
                var cheatList = traverse.Field("cheatMenuItems").GetValue<List<DuskersMenuItem>>();
                if (cheatList != null)
                {
                    cheatList.Clear();
                    cheatList.Add(new DuskersMenuItem("[R]eset Universe", KeyCode.R, (item) =>
                    {
                        traverse.Method("MenuResetUniverse", new object[] { item }).GetValue();
                    }, num++));
                    cheatList.Add(new DuskersMenuItem("[A]rchive Data", KeyCode.A, (item) =>
                    {
                        traverse.Method("MenuPackageData", new object[] { item }).GetValue();
                    }, num++));
                }

                ObjectiveManual.Reset();
                Application.runInBackground = GameSaveFile.Get("O_RIB", false);

                // Call base.LoadMenu()
                MenuPanelUI.Instance.PushMenu(__instance);
                traverse.Property("IsLoaded").SetValue(true);

                bool relaunchChallenge = false;
                try { relaunchChallenge = traverse.Property("RelaunchChallengeMenu").GetValue<bool>(); } catch { }

                bool relaunchOptions = false;
                try { relaunchOptions = traverse.Property("RelaunchOptionsMenu").GetValue<bool>(); } catch { }

                if (GlobalSettings.ShowDailyLeaderboard || GlobalSettings.ShowWeeklyLeaderboard || relaunchChallenge)
                {
                    traverse.Property("RelaunchChallengeMenu").SetValue(false);
                    traverse.Method("MenuPlayChallenge", new object[] { null }).GetValue();
                }
                else if (relaunchOptions)
                {
                    traverse.Property("RelaunchOptionsMenu").SetValue(false);
                    traverse.Method("MenuOptions", new object[] { null }).GetValue();
                }
                else
                {
                    GalaxyMapManager.ReleaseReferencesOnMainMenu();
                }

                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DuskersCoopMod] Error in MainMenu.LoadMenu prefix: {ex}");
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(MenuPanelUI))]
    public static class MenuPanelUIPatches
    {
        [HarmonyPrefix]
        [HarmonyPatch("PopMenu", new Type[] { typeof(MenuScreenClass) })]
        public static bool PopMenu_Prefix(MenuPanelUI __instance, MenuScreenClass menu)
        {
            try
            {
                if (__instance == null || menu == null) return false;
                var tr = Traverse.Create(__instance);
                var stack = tr.Field("screenStack").GetValue<System.Collections.IList>();
                if (stack == null || stack.Count == 0)
                {
                    try { Traverse.Create(__instance).Method("CloseMenu")?.GetValue(); } catch { }
                    return false;
                }

                bool found = false;
                for (int i = 0; i < stack.Count; i++)
                {
                    if (stack[i] == menu)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DuskersCoopMod] Handled PopMenu safely: {ex.Message}");
                return false;
            }
            return true;
        }
    }
}
