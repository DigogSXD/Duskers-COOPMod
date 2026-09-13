using System;
using System.Reflection;
using DuskersCoopMod.Network;
using DuskersCoopMod.Save;
using HarmonyLib;
using UnityEngine;

namespace DuskersCoopMod.Patches
{
    [HarmonyPatch]
    public static class UniverseMapPatches
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(UniverseMapManager), "ChooseStartingGalaxy")]
        public static bool ChooseStartingGalaxy_Prefix(UniverseMapManager __instance)
        {
            // Always zero DestinationGalaxyOverride.
            // Duskers has an unhandled native bug in ChooseStartingGalaxy: if DestinationGalaxyOverride != 0 and the ID is not
            // found in placedNodes, it logs an error and dereferences a null UniverseNode, causing a fatal crash.
            try
            {
                Traverse.Create(__instance).Property("DestinationGalaxyOverride")?.SetValue(0);
                Traverse.Create(__instance).Field("<DestinationGalaxyOverride>k__BackingField")?.SetValue(0);
            }
            catch {}

            var net = CoopNetworkManager.Instance;
            if (net != null && net.Role == NetworkRole.Client)
            {
                try
                {
                    var tr = Traverse.Create(__instance);
                    var placedNodes = tr.Field("placedNodes").GetValue<System.Collections.Generic.List<UniverseNode>>();
                    if (placedNodes != null && placedNodes.Count > 0)
                    {
                        UniverseNode targetNode = null;
                        if (CoopGalaxySyncManager.HasTargetGalaxyState && CoopGalaxySyncManager.TargetGalaxyId != 0)
                        {
                            targetNode = placedNodes.Find(n => n != null && n.InternalID == CoopGalaxySyncManager.TargetGalaxyId);
                        }

                        if (targetNode == null)
                        {
                            int curGlxy = 0;
                            try { curGlxy = UniverseSaveFile.Get<int>("CUR_GLXY", 0); } catch {}
                            if (curGlxy != 0)
                            {
                                targetNode = placedNodes.Find(n => n != null && n.InternalID == curGlxy);
                            }
                        }

                        if (targetNode == null)
                        {
                            targetNode = placedNodes[0];
                        }

                        if (targetNode != null)
                        {
                            if (__instance.CurrentUniverseNode != null)
                            {
                                __instance.CurrentUniverseNode.IsSelected = false;
                            }

                            if (!__instance.IsReadOnlyGalaxy)
                            {
                                try
                                {
                                    UniverseSaveFile.Add("GHOP", targetNode.GroupKey);
                                    UniverseSaveFile.Save<string>(targetNode.GroupKey, "FILE", string.Format("gd_{0}", targetNode.InternalID));
                                    UniverseSaveFile.Save<int>("CUR_GLXY", targetNode.InternalID);
                                }
                                catch {}
                            }

                            targetNode.IsSelected = true;
                            tr.Property("CurrentUniverseNode").SetValue(targetNode);

                            if (targetNode.constellation != null)
                            {
                                tr.Method("UpdateConstelationDataStates")?.GetValue();
                            }

                            GalaxySaveFile.InitSetting(targetNode.InternalID);
                            string mapChoosen = tr.Method("AssignGalaxyMapToNode", new object[] { targetNode })?.GetValue<string>();
                            if (!string.IsNullOrEmpty(mapChoosen))
                            {
                                GameSaveFile.Save<string>("GALAXY_ID", mapChoosen);
                            }

                            Debug.Log($"[DuskersCoopMod] [Client] ChooseStartingGalaxy successfully selected node {targetNode.InternalID} ({targetNode.name})");
                            return false; // Safely bypass native ChooseStartingGalaxy!
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[DuskersCoopMod] Error in custom ChooseStartingGalaxy for client: {ex}");
                }
            }

            return true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(UniverseMapManager), "ChooseStartingGalaxy")]
        public static void ChooseStartingGalaxy_Postfix(UniverseMapManager __instance)
        {
            if (__instance == null) return;
            if (__instance.CurrentUniverseNode == null)
            {
                try
                {
                    var tr = Traverse.Create(__instance);
                    var placedNodes = tr.Field("placedNodes").GetValue<System.Collections.Generic.List<UniverseNode>>();
                    if (placedNodes != null && placedNodes.Count > 0)
                    {
                        var node = placedNodes[0];
                        node.IsSelected = true;
                        tr.Property("CurrentUniverseNode").SetValue(node);
                        GalaxySaveFile.InitSetting(node.InternalID);
                        tr.Method("AssignGalaxyMapToNode", new object[] { node })?.GetValue();
                        Debug.LogWarning($"[DuskersCoopMod] ChooseStartingGalaxy had null CurrentUniverseNode, safely fell back to node {node.InternalID}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[DuskersCoopMod] Error in ChooseStartingGalaxy_Postfix fallback: {ex}");
                }
            }
        }
    }

    [HarmonyPatch(typeof(StarSystemInfo))]
    public static class StarSystemInfoPatches
    {
        [ThreadStatic]
        private static bool _isBuildingFallback = false;

        [HarmonyPostfix]
        [HarmonyPatch("get_Dungeons")]
        public static void get_Dungeons_Postfix(StarSystemInfo __instance, ref System.Collections.Generic.List<DungeonInfo> __result)
        {
            if (_isBuildingFallback) return;
            if (__instance == null) return;
            if (__result == null)
            {
                __result = new System.Collections.Generic.List<DungeonInfo>();
                __instance.Dungeons = __result;
            }

            if (__result.Count == 0)
            {
                _isBuildingFallback = true;
                try
                {
                    int seed = UnityEngine.Random.Range(10000, 999999);
                    var d = GalaxyProcessor.BuildNormalDungeon(1, DungeonTypeEnum.Derelict, __instance, seed, 1);
                    if (d != null)
                    {
                        d.Parent = __instance;
                        d.HaveVisited = false;
                        __result.Add(d);
                        Debug.LogWarning($"[DuskersCoopMod] Guaranteed fallback derelict in StarSystemInfo.Dungeons for {__instance.Name} (seed: {seed})");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[DuskersCoopMod] Error creating fallback dungeon in get_Dungeons: {ex}");
                }
                finally
                {
                    _isBuildingFallback = false;
                }
            }
        }
    }

    [HarmonyPatch]
    public static class GalaxyProcessorPatches
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GalaxyProcessor), "GenerateDungeonInfo", new Type[] { typeof(StarSystemInfo), typeof(bool), typeof(GalaxyProcessor.DungeonProcessorCB) })]
        public static void GenerateDungeonInfo_Postfix(StarSystemInfo starSystemInfo)
        {
            EnsureSystemHasDungeons(starSystemInfo);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GalaxyProcessor), "GenerateNurseryDungeonsFromData", new Type[] { typeof(StarSystemInfo) })]
        public static void GenerateNurseryDungeonsFromData_Postfix(StarSystemInfo starSystemInfo)
        {
            EnsureSystemHasDungeons(starSystemInfo);
        }

        [ThreadStatic]
        private static bool _isEnsuring = false;

        public static void EnsureSystemHasDungeons(StarSystemInfo starSystemInfo)
        {
            if (_isEnsuring) return;
            if (starSystemInfo == null) return;
            _isEnsuring = true;
            try
            {
                GalaxyMapPatches.RepairNurseryKeys();
                if (starSystemInfo.Dungeons == null)
                {
                    starSystemInfo.Dungeons = new System.Collections.Generic.List<DungeonInfo>();
                }

                if (starSystemInfo.Dungeons.Count == 0)
                {
                    int seed = UnityEngine.Random.Range(10000, 999999);
                    var d = GalaxyProcessor.BuildNormalDungeon(1, DungeonTypeEnum.Derelict, starSystemInfo, seed, 1);
                    if (d != null)
                    {
                        d.Parent = starSystemInfo;
                        d.HaveVisited = false;
                        starSystemInfo.Dungeons.Add(d);
                        Debug.LogWarning($"[DuskersCoopMod] Injected fallback derelict into empty system {starSystemInfo.Name} (seed: {seed})");
                    }
                }

                // Repair any dungeons with Unknown type so GalaxyMapManager won't crash
                foreach (var dung in starSystemInfo.Dungeons)
                {
                    if (dung != null && (dung.DungeonType == DungeonTypeEnum.Unknown || (int)dung.DungeonType == 0))
                    {
                        dung.DungeonType = DungeonTypeEnum.Derelict;
                        Debug.LogWarning($"[DuskersCoopMod] Repaired Unknown dungeon type to Derelict for {dung.Name} in {starSystemInfo.Name}");
                    }
                }

                if (starSystemInfo.Dungeons.Count > 0)
                {
                    if (!starSystemInfo.Dungeons.Exists(d => d != null && !d.HaveVisited))
                    {
                        starSystemInfo.Dungeons[0].HaveVisited = false;
                        if (!string.IsNullOrEmpty(starSystemInfo.Dungeons[0].GroupKey))
                        {
                            GalaxySaveFile.Save<bool>(starSystemInfo.Dungeons[0].GroupKey, "VISITED", false);
                        }
                    }

                    var target = starSystemInfo.Dungeons.Find(d => d != null && !d.HaveVisited) ?? starSystemInfo.Dungeons[0];
                    if (target != null && !string.IsNullOrEmpty(target.GroupKey))
                    {
                        GalaxySaveFile.Save<string>(starSystemInfo.GroupKey, "LAST_DOCKED_ID", target.GroupKey);
                        GalaxySaveFile.Save<string>(starSystemInfo.GroupKey, "LAST_SELECTED_ID", target.GroupKey);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DuskersCoopMod] Error in EnsureSystemHasDungeons: {ex}");
            }
            finally
            {
                _isEnsuring = false;
            }
        }
    }

    [HarmonyPatch(typeof(GalaxyMapManager))]
    public static class GalaxyMapPatches
    {
        public static bool IsApplyingRemoteAction = false;

        [HarmonyPostfix]
        [HarmonyPatch("Start")]
        public static void Start_Postfix()
        {
            var net = CoopNetworkManager.Instance;
            if (net != null && net.Role == NetworkRole.Host && net.ConnectedCount > 0)
            {
                CoopSaveSyncManager.SyncToAllClients();
                CoopGalaxySyncManager.BroadcastGalaxyState();
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("DetermineStartupStarSystem", new Type[] { typeof(bool) })]
        public static void DetermineStartupStarSystem_Postfix(GalaxyMapManager __instance)
        {
            var net = CoopNetworkManager.Instance;
            if (net != null && net.Role == NetworkRole.Client && CoopGalaxySyncManager.HasTargetGalaxyState)
            {
                try
                {
                    var tr = Traverse.Create(__instance);
                    var nodes = tr.Field("_starSystemNodes").GetValue<System.Collections.IList>();
                    StarSystemInfo targetSys = null;

                    if (nodes != null)
                    {
                        foreach (object node in nodes)
                        {
                            var info = Traverse.Create(node).Property("Info").GetValue<StarSystemInfo>();
                            if (info != null && (
                                (!string.IsNullOrEmpty(CoopGalaxySyncManager.TargetSystemName) && string.Equals(info.Name, CoopGalaxySyncManager.TargetSystemName, StringComparison.OrdinalIgnoreCase)) ||
                                (CoopGalaxySyncManager.TargetSystemId != 0 && info.Id == CoopGalaxySyncManager.TargetSystemId)))
                            {
                                targetSys = info;
                                break;
                            }
                        }
                    }

                    if (targetSys != null)
                    {
                        CoopGalaxySyncManager.SetShipStarSystem(__instance, targetSys, true);

                        if (!string.IsNullOrEmpty(CoopGalaxySyncManager.TargetDockedDungeon) && targetSys.Dungeons != null)
                        {
                            var targetDung = targetSys.Dungeons.Find(d =>
                                (!string.IsNullOrEmpty(d.DisplayName) && d.DisplayName.Equals(CoopGalaxySyncManager.TargetDockedDungeon, StringComparison.OrdinalIgnoreCase)) ||
                                (!string.IsNullOrEmpty(d.Name) && d.Name.Equals(CoopGalaxySyncManager.TargetDockedDungeon, StringComparison.OrdinalIgnoreCase)) ||
                                d.Id.ToString() == CoopGalaxySyncManager.TargetDockedDungeon);

                            if (targetDung != null)
                            {
                                CoopGalaxySyncManager.SetShipDungeon(__instance, targetDung, true);
                            }
                        }

                        tr.Method("UpdateGUIVariables")?.GetValue();
                        Debug.Log($"[DuskersCoopMod] Successfully aligned Client to Host Star System: {targetSys.Name}, Derelict: {CoopGalaxySyncManager.TargetDockedDungeon}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[DuskersCoopMod] Error in DetermineStartupStarSystem_Postfix (client alignment): {ex}");
                }
            }

            // Safe Startup Dungeon Guard (Protects BOTH Host and Client against native Duskers Enumerable.First() crash in DoAwake)
            try
            {
                var player = GlobalSettings.GameState != null ? GlobalSettings.GameState.ThePlayer : null;
                if (player != null)
                {
                    var curSys = player.CurrentStarSystem;
                    var tr = Traverse.Create(__instance);

                    if (curSys == null)
                    {
                        var nodes = tr.Field("_starSystemNodes").GetValue<System.Collections.IList>();
                        if (nodes != null && nodes.Count > 0)
                        {
                            var firstInfo = Traverse.Create(nodes[0]).Property("Info").GetValue<StarSystemInfo>();
                            if (firstInfo != null)
                            {
                                player.CurrentStarSystem = firstInfo;
                                curSys = firstInfo;
                            }
                        }
                    }

                    if (curSys != null)
                    {
                        if (curSys.Dungeons == null || curSys.Dungeons.Count == 0)
                        {
                            GalaxyProcessor.GenerateDungeonInfo(curSys, true, null);
                        }

                        if (curSys.Dungeons != null && curSys.Dungeons.Count > 0)
                        {
                            string groupKey = curSys.GroupKey;
                            string lastDocked = GalaxySaveFile.Get<string>(groupKey, "LAST_DOCKED_ID", string.Empty);
                            if (string.IsNullOrEmpty(lastDocked) || !curSys.Dungeons.Exists(d => d != null && d.GroupKey == lastDocked))
                            {
                                var targetDung = curSys.Dungeons.Find(d => d != null && !d.HaveVisited) ?? curSys.Dungeons[0];
                                if (targetDung != null)
                                {
                                    GalaxySaveFile.Save<string>(groupKey, "LAST_DOCKED_ID", targetDung.GroupKey);
                                    GalaxySaveFile.Save<string>(groupKey, "LAST_SELECTED_ID", targetDung.GroupKey);
                                    Debug.Log($"[DuskersCoopMod] Guaranteed LAST_DOCKED_ID={targetDung.GroupKey} for system {curSys.Name} to protect against native crash.");
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DuskersCoopMod] Error in Safe Startup Dungeon Guard: {ex}");
            }
        }

        public static void RepairNurseryKeys()
        {
            try
            {
                var uniGroups = UniverseSaveFile.GetAllGroups("OBJN_");
                if (uniGroups != null)
                {
                    int fallbackIdx = 0;
                    foreach (var g in uniGroups)
                    {
                        if (string.IsNullOrEmpty(g)) continue;
                        int idx = UniverseSaveFile.Get<int>(g, "EPIDX", -1);
                        if (idx < 0 || idx >= 4)
                        {
                            int safeIdx = fallbackIdx % 4;
                            UniverseSaveFile.Save<int>(g, "EPIDX", safeIdx);
                            Debug.LogWarning($"[DuskersCoopMod] Repaired invalid earlyPlayIdx in UniverseSaveFile ({idx} -> {safeIdx}) for {g}");
                            fallbackIdx++;
                        }

                        string dest = g.Replace("OBJN_", "OBJ_");
                        UniverseSaveFile.Save<int>(dest, "EPIDX", (fallbackIdx > 0 ? fallbackIdx - 1 : 0) % 4);
                        GalaxySaveFile.Save<int>(dest, "EPIDX", (fallbackIdx > 0 ? fallbackIdx - 1 : 0) % 4);
                    }
                }

                var galNursery = GalaxySaveFile.GetAllGroups("OBJN_");
                if (galNursery != null)
                {
                    int fallbackIdx = 0;
                    foreach (var g in galNursery)
                    {
                        if (string.IsNullOrEmpty(g)) continue;
                        int idx = GalaxySaveFile.Get<int>(g, "EPIDX", -1);
                        if (idx < 0 || idx >= 4)
                        {
                            int safeIdx = fallbackIdx % 4;
                            GalaxySaveFile.Save<int>(g, "EPIDX", safeIdx);
                            Debug.LogWarning($"[DuskersCoopMod] Repaired invalid earlyPlayIdx in GalaxySaveFile ({idx} -> {safeIdx}) for {g}");
                            fallbackIdx++;
                        }
                    }
                }

                var galObj = GalaxySaveFile.GetAllGroups("OBJ_");
                if (galObj != null)
                {
                    int fallbackIdx = 0;
                    foreach (var g in galObj)
                    {
                        if (string.IsNullOrEmpty(g)) continue;
                        int idx = GalaxySaveFile.Get<int>(g, "EPIDX", -1);
                        if (idx < 0 || idx >= 4)
                        {
                            int safeIdx = fallbackIdx % 4;
                            GalaxySaveFile.Save<int>(g, "EPIDX", safeIdx);
                            fallbackIdx++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DuskersCoopMod] Error in RepairNurseryKeys: {ex}");
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch("DoAwake")]
        public static void DoAwake_Prefix()
        {
            try
            {
                RepairNurseryKeys();
                var p = GlobalSettings.GameState != null ? GlobalSettings.GameState.ThePlayer : null;
                if (p != null && p.CurrentStarSystem != null)
                {
                    GalaxyProcessorPatches.EnsureSystemHasDungeons(p.CurrentStarSystem);
                }
            }
            catch {}
        }

        [HarmonyFinalizer]
        [HarmonyPatch("DoAwake")]
        public static Exception DoAwake_Finalizer(Exception __exception, GalaxyMapManager __instance)
        {
            if (__exception != null)
            {
                Debug.LogError($"[DuskersCoopMod] Caught and suppressed fatal error in GalaxyMapManager.DoAwake: {__exception}");
                try
                {
                    var p = GlobalSettings.GameState != null ? GlobalSettings.GameState.ThePlayer : null;
                    if (p != null && p.CurrentStarSystem != null)
                    {
                        GalaxyProcessorPatches.EnsureSystemHasDungeons(p.CurrentStarSystem);
                        if (p.CurrentStarSystem.Dungeons != null && p.CurrentStarSystem.Dungeons.Count > 0)
                        {
                            var d = p.CurrentStarSystem.Dungeons[0];
                            Traverse.Create(__instance).Method("SetPlayerShipDungeon", new object[] { d, true })?.GetValue();
                            Traverse.Create(__instance).Property("SelectedDungeon")?.SetValue(d);
                        }
                    }
                }
                catch {}
                return null; // Suppresses exception to prevent CrashHandler.CrashAndQuit from closing the game!
            }
            return null;
        }

        [HarmonyFinalizer]
        [HarmonyPatch("Start")]
        public static Exception Start_Finalizer(Exception __exception)
        {
            if (__exception != null)
            {
                Debug.LogWarning($"[DuskersCoopMod] Suppressed exception in GalaxyMapManager.Start: {__exception}");
                return null;
            }
            return null;
        }

        [HarmonyFinalizer]
        [HarmonyPatch("UpdateAllDungeonVisualDistanceIndications", new Type[] { typeof(StarSystemInfo) })]
        public static Exception UpdateAllDungeonVisualDistanceIndications_Finalizer(Exception __exception)
        {
            return null;
        }

        [HarmonyFinalizer]
        [HarmonyPatch("UpdateAllDungeonVisualDistanceIndications", new Type[] { })]
        public static Exception UpdateAllDungeonVisualDistanceIndications_Empty_Finalizer(Exception __exception)
        {
            return null;
        }

        [HarmonyPrefix]
        [HarmonyPatch("SetSelectedDungeon", new Type[] { typeof(DungeonInfo), typeof(bool) })]
        public static bool SetSelectedDungeon_Prefix(GalaxyMapManager __instance, ref DungeonInfo dungeon)
        {
            if (dungeon == null)
            {
                var curSys = GlobalSettings.GameState != null && GlobalSettings.GameState.ThePlayer != null ? GlobalSettings.GameState.ThePlayer.CurrentStarSystem : null;
                if (curSys != null && curSys.Dungeons != null && curSys.Dungeons.Count > 0)
                {
                    dungeon = curSys.Dungeons[0];
                }
            }
            if (dungeon != null)
            {
                Traverse.Create(__instance).Property("SelectedDungeon")?.SetValue(dungeon);
            }
            return dungeon != null;
        }

        [HarmonyFinalizer]
        [HarmonyPatch("DoUpdate")]
        public static Exception DoUpdate_Finalizer(Exception __exception)
        {
            if (__exception != null)
            {
                // Suppress unhandled frame exceptions to prevent CrashHandler.CrashAndQuit from displaying fatal dialog
                return null;
            }
            return null;
        }

        [HarmonyPrefix]
        [HarmonyPatch("TravelToDungeon", new Type[] { typeof(bool) })]
        public static bool TravelToDungeon_Prefix(bool force)
        {
            if (IsApplyingRemoteAction) return true;
            var net = CoopNetworkManager.Instance;
            if (net != null && net.Role == NetworkRole.Client)
            {
                net.PrintToLocalConsole("[COOP] Derelict navigation is controlled by the Host operator.", ConsoleMessageType.Warning);
                return false;
            }
            return true;
        }

        [HarmonyPostfix]
        [HarmonyPatch("TravelToDungeon", new Type[] { typeof(bool) })]
        public static void TravelToDungeon_Postfix(GalaxyMapManager __instance, bool force)
        {
            if (IsApplyingRemoteAction) return;
            var net = CoopNetworkManager.Instance;
            if (net != null && net.Role == NetworkRole.Host && net.ConnectedCount > 0)
            {
                var selDungeon = Traverse.Create(__instance).Property("SelectedDungeon").GetValue<DungeonInfo>();
                string targetDungeonName = "";
                if (selDungeon != null)
                {
                    targetDungeonName = !string.IsNullOrEmpty(selDungeon.DisplayName)
                        ? selDungeon.DisplayName
                        : (!string.IsNullOrEmpty(selDungeon.Name) ? selDungeon.Name : selDungeon.Id.ToString());
                }
                net.BroadcastPacket(PacketWrapper.Create("STRATEGIC_ACTION", "Host", new StrategicActionData
                {
                    action = "TRAVEL_DUNGEON",
                    targetName = targetDungeonName
                }));
                CoopGalaxySyncManager.BroadcastGalaxyState();
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch("TravelToStarSystem", new Type[] { })]
        public static bool TravelToStarSystem_Prefix()
        {
            if (IsApplyingRemoteAction) return true;
            var net = CoopNetworkManager.Instance;
            if (net != null && net.Role == NetworkRole.Client)
            {
                net.PrintToLocalConsole("[COOP] Star system travel is controlled by the Host operator.", ConsoleMessageType.Warning);
                return false;
            }
            return true;
        }

        [HarmonyPostfix]
        [HarmonyPatch("TravelToStarSystem", new Type[] { })]
        public static void TravelToStarSystem_Postfix(GalaxyMapManager __instance)
        {
            if (IsApplyingRemoteAction) return;
            var net = CoopNetworkManager.Instance;
            if (net != null && net.Role == NetworkRole.Host && net.ConnectedCount > 0)
            {
                var selSys = Traverse.Create(__instance).Property("SelectedStarSystem").GetValue<StarSystemInfo>();
                string sysName = selSys != null ? selSys.Name : (Traverse.Create(__instance).Field("guiCurrentSystemName").GetValue<string>() ?? "");
                net.BroadcastPacket(PacketWrapper.Create("STRATEGIC_ACTION", "Host", new StrategicActionData
                {
                    action = "TRAVEL_SYSTEM",
                    targetName = sysName
                }));
                CoopGalaxySyncManager.BroadcastGalaxyState();
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("ShowStarSystemView", new Type[] { typeof(StarSystemInfo), typeof(bool), typeof(bool) })]
        public static void ShowStarSystemView_Postfix(GalaxyMapManager __instance, StarSystemInfo starSystem, bool viewOnly, bool ignoreSound)
        {
            if (IsApplyingRemoteAction) return;
            var net = CoopNetworkManager.Instance;
            if (net != null && net.Role == NetworkRole.Host && net.ConnectedCount > 0 && starSystem != null)
            {
                string sysName = !string.IsNullOrEmpty(starSystem.Name) ? starSystem.Name : starSystem.Id.ToString();
                net.BroadcastPacket(PacketWrapper.Create("STRATEGIC_ACTION", "Host", new StrategicActionData
                {
                    action = "SHOW_SYSTEM_VIEW",
                    targetName = sysName
                }));
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("HideStarSystemView")]
        public static void HideStarSystemView_Postfix()
        {
            if (IsApplyingRemoteAction) return;
            var net = CoopNetworkManager.Instance;
            if (net != null && net.Role == NetworkRole.Host && net.ConnectedCount > 0)
            {
                net.BroadcastPacket(PacketWrapper.Create("STRATEGIC_ACTION", "Host", new StrategicActionData
                {
                    action = "HIDE_SYSTEM_VIEW"
                }));
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch("ConfirmJump", new Type[] { typeof(ModalWindowResult), typeof(string) })]
        public static bool ConfirmJump_Prefix(ModalWindowResult result, string input)
        {
            if (IsApplyingRemoteAction) return true;
            var net = CoopNetworkManager.Instance;
            if (net != null && net.Role == NetworkRole.Client)
            {
                net.PrintToLocalConsole("[COOP] Galaxy jumps are controlled by the Host operator.", ConsoleMessageType.Warning);
                return false;
            }
            return true;
        }

        [HarmonyPostfix]
        [HarmonyPatch("ConfirmJump", new Type[] { typeof(ModalWindowResult), typeof(string) })]
        public static void ConfirmJump_Postfix(ModalWindowResult result, string input)
        {
            if (IsApplyingRemoteAction) return;
            if (result != ModalWindowResult.Yes) return;

            var net = CoopNetworkManager.Instance;
            if (net != null && net.Role == NetworkRole.Host && net.ConnectedCount > 0)
            {
                CoopSaveSyncManager.SyncToAllClients();
                net.BroadcastPacket(PacketWrapper.Create("STRATEGIC_ACTION", "Host", new StrategicActionData
                {
                    action = "JUMP",
                    targetName = input
                }));
                CoopGalaxySyncManager.BroadcastGalaxyState();
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch("BoardCurrentDungeon")]
        public static bool BoardCurrentDungeon_Prefix(GalaxyMapManager __instance)
        {
            var net = CoopNetworkManager.Instance;
            if (!IsApplyingRemoteAction && net != null && net.Role == NetworkRole.Client)
            {
                net.PrintToLocalConsole("[COOP] Derelict boarding is initiated by the Host operator.", ConsoleMessageType.Warning);
                return false;
            }

            try
            {
                PerformSafeBoarding(__instance);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DuskersCoopMod] Error in PerformSafeBoarding: {ex}");
            }

            return false;
        }

        public static void PerformSafeBoarding(GalaxyMapManager gmm)
        {
            var net = CoopNetworkManager.Instance;
            if (!IsApplyingRemoteAction && net != null && net.Role == NetworkRole.Host && net.ConnectedCount > 0)
            {
                // Sync latest save so derelict seed and fleet state match 100%
                CoopSaveSyncManager.SyncToAllClients();

                var selDung = (gmm != null ? gmm.SelectedDungeon : null)
                    ?? GlobalSettings.GameState?.ThePlayer?.CurrentDockedDungeon;

                string dungName = "";
                string dungGroup = "";
                if (selDung != null)
                {
                    dungName = !string.IsNullOrEmpty(selDung.DisplayName) ? selDung.DisplayName : selDung.Name;
                    dungGroup = selDung.GroupKey ?? "";
                }

                int dungSeed = 0;
                if (selDung != null && !string.IsNullOrEmpty(selDung.GroupKey))
                {
                    dungSeed = GalaxySaveFile.Get(selDung.GroupKey, "SEED_D", -1);
                }
                if (dungSeed <= 0)
                {
                    dungSeed = UnityEngine.Random.Range(100000, 999999);
                    if (selDung != null && !string.IsNullOrEmpty(selDung.GroupKey))
                    {
                        GalaxySaveFile.Save(selDung.GroupKey, "SEED_D", dungSeed);
                    }
                }

                DungeonPatches.SynchronizedDungeonSeed = dungSeed;
                UnityEngine.Random.seed = dungSeed;

                net.BroadcastPacket(PacketWrapper.Create("STRATEGIC_ACTION", "Host", new StrategicActionData
                {
                    action = "BOARD_DUNGEON",
                    targetName = dungName,
                    dungeonGroup = dungGroup,
                    dungeonSeed = dungSeed
                }));
            }

            // Safely clear event listeners
            if (gmm != null && GlobalSettings.GameState?.StarSystems != null)
            {
                foreach (var sys in GlobalSettings.GameState.StarSystems)
                {
                    if (sys == null) continue;
                    sys.OnStarSystemEvent = null;
                    if (sys.Dungeons != null)
                    {
                        foreach (var d in sys.Dungeons)
                        {
                            if (d != null) d.OnDungeonEvent = null;
                        }
                    }
                    if (sys.galaxyNode != null)
                    {
                        sys.galaxyNode.shortcutPressed = null;
                        if (sys.galaxyNode.DungeonNodes != null)
                        {
                            foreach (var dn in sys.galaxyNode.DungeonNodes)
                            {
                                if (dn != null) dn.shortcutPressed = null;
                            }
                        }
                    }
                }
            }

            GlobalSettings.GameStartedFromGalaxyMap = true;
            var player = GlobalSettings.GameState?.ThePlayer;
            var dungeon = (gmm != null ? gmm.SelectedDungeon : null) ?? player?.CurrentDockedDungeon;

            if (dungeon != null)
            {
                dungeon.HaveVisited = true;
                if (dungeon.Parent != null && dungeon.Parent.IsNursery && GameSaveFile.Get<bool>("NC", false))
                {
                    try { Traverse.Create(gmm).Method("SyncNurseryDataBetweenDataFiles")?.GetValue(); } catch { }
                }
                UniverseSaveFile.Save<int>("STAT_VDUN", UniverseSaveFile.Get<int>("STAT_VDUN", 0) + 1);
                if (!string.IsNullOrEmpty(dungeon.GroupKey))
                {
                    GalaxySaveFile.Save<bool>(dungeon.GroupKey, "VISITED", true);
                }
                if (dungeon.Parent != null)
                {
                    try { dungeon.Parent.Refresh(); } catch { }
                }
                if (player != null)
                {
                    player.CurrentDockedDungeon = dungeon;
                    if (dungeon.Parent != null)
                    {
                        player.CurrentStarSystem = dungeon.Parent;
                    }
                }
            }

            var curSys = player?.CurrentStarSystem;
            if (curSys != null && !string.IsNullOrEmpty(curSys.GroupKey))
            {
                if (!GalaxySaveFile.Get<bool>(curSys.GroupKey, "VISITED", false))
                {
                    UniverseSaveFile.Save<int>("STAT_VSYS", UniverseSaveFile.Get<int>("STAT_VSYS", 0) + 1);
                    if (!GlobalSettings.IsTutorial)
                    {
                        GalaxySaveFile.Save<bool>(curSys.GroupKey, "VISITED", true);
                    }
                }
            }

            if (!GlobalSettings.IsTutorial)
            {
                GameSaveFile.Save<int>("MISSIONS", GameSaveFile.Get<int>("MISSIONS", 0) + 1);
            }

            var myShip = player?.MyShip;
            if (myShip != null && myShip.InstalledInventory != null && myShip.InstalledInventory.ItemsCopy != null)
            {
                foreach (var item in myShip.InstalledInventory.ItemsCopy)
                {
                    if (item is BaseShipUpgrade up)
                    {
                        up.UsedMissionCount++;
                    }
                }
            }

            GlobalSettings.NumLogsAfterTutorial++;
            GlobalSettings.cheatMode = false;
            if (gmm != null)
            {
                Traverse.Create(gmm).Field("isLoadingScene").SetValue(true);
                try
                {
                    int upgradesCount = Traverse.Create(gmm).Method("GetTotalShipUpgradesCount").GetValue<int>();
                    Traverse.Create(typeof(GalaxyMapManager)).Field("_shipUpgradesCountPriorToMission").SetValue(upgradesCount);
                }
                catch { }
            }
            GalaxyMapManager.hasBoardedDungeon = true;
            if (player?.Inventory != null)
            {
                Traverse.Create(typeof(GalaxyMapManager)).Field("scrapAtBoard").SetValue(player.Inventory.Scrap);
            }
            GameSaveFile.Save<bool>("MSG_DJ", true);

            if (Mothership.Instance != null)
            {
                try { Mothership.Instance.Stop(); } catch { }
            }

            string scene = (dungeon != null && !string.IsNullOrEmpty(dungeon.SceneName))
                ? dungeon.SceneName
                : "DungeonScene_Generated_Pro";

            Debug.Log($"[DuskersCoopMod] Boarding derelict '{(dungeon != null ? (!string.IsNullOrEmpty(dungeon.DisplayName) ? dungeon.DisplayName : dungeon.Name) : "Unknown")}' (Scene: {scene})...");
            UnityEngine.Application.LoadLevel(scene);
        }

        [HarmonyPostfix]
        [HarmonyPatch("SetMapState", new Type[] { typeof(GalaxyMapState), typeof(bool), typeof(bool) })]
        public static void SetMapState_Postfix(GalaxyMapState state, bool force, bool ignoreSound)
        {
            if (IsApplyingRemoteAction) return;
            var net = CoopNetworkManager.Instance;
            if (net != null && net.Role == NetworkRole.Host && net.ConnectedCount > 0)
            {
                net.BroadcastPacket(PacketWrapper.Create("STRATEGIC_ACTION", "Host", new StrategicActionData
                {
                    action = "SET_MAP_STATE",
                    targetName = ((int)state).ToString()
                }));
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("CloseModificationsButtonPressed")]
        public static void CloseModificationsButtonPressed_Postfix()
        {
            var net = CoopNetworkManager.Instance;
            if (net != null && net.Role == NetworkRole.Host && net.ConnectedCount > 0)
            {
                CoopSaveSyncManager.SyncToAllClients();
                CoopGalaxySyncManager.BroadcastGalaxyState();
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("CloseShipUpgradeButtonPressed")]
        public static void CloseShipUpgradeButtonPressed_Postfix()
        {
            var net = CoopNetworkManager.Instance;
            if (net != null && net.Role == NetworkRole.Host && net.ConnectedCount > 0)
            {
                CoopSaveSyncManager.SyncToAllClients();
                CoopGalaxySyncManager.BroadcastGalaxyState();
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("CloseTradingPostButtonPressed")]
        public static void CloseTradingPostButtonPressed_Postfix()
        {
            var net = CoopNetworkManager.Instance;
            if (net != null && net.Role == NetworkRole.Host && net.ConnectedCount > 0)
            {
                CoopSaveSyncManager.SyncToAllClients();
                CoopGalaxySyncManager.BroadcastGalaxyState();
            }
        }
    }

    [HarmonyPatch(typeof(GalaxyMapManager), "SetSelectedDungeon", new Type[] { typeof(DungeonInfo), typeof(bool) })]
    public static class GalaxyMapManager_SetSelectedDungeon_Patch
    {
        [HarmonyFinalizer]
        public static Exception Finalizer(Exception __exception)
        {
            if (__exception != null)
            {
                Debug.LogWarning($"[DuskersCoopMod] Handled exception in GalaxyMapManager.SetSelectedDungeon: {__exception.Message}");
            }
            return null;
        }
    }

    [HarmonyPatch(typeof(DroneManager))]
    public static class DroneManagerPatches
    {
        [HarmonyFinalizer]
        [HarmonyPatch("Update")]
        public static Exception Update_Finalizer(Exception __exception)
        {
            // Suppress NRE during scene transitions / mission exit when CurrentDrone or dronesList is torn down
            return null;
        }
    }

    [HarmonyPatch(typeof(GalaxyProcessor), "GenerateNurseryDungeonsFromData")]
    public static class GalaxyProcessor_GenerateNurseryDungeonsFromData_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(StarSystemInfo starSystemInfo)
        {
            GalaxyMapPatches.RepairNurseryKeys();
        }

        [HarmonyFinalizer]
        public static Exception Finalizer(Exception __exception, StarSystemInfo starSystemInfo)
        {
            if (__exception != null)
            {
                Debug.LogWarning($"[DuskersCoopMod] Handled exception in GenerateNurseryDungeonsFromData: {__exception.Message}");
                GalaxyProcessorPatches.EnsureSystemHasDungeons(starSystemInfo);
            }
            return null;
        }
    }
}
