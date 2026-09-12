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
        public static void ChooseStartingGalaxy_Prefix(UniverseMapManager __instance)
        {
            var net = CoopNetworkManager.Instance;
            if (net != null && net.Role == NetworkRole.Client && CoopGalaxySyncManager.HasTargetGalaxyState)
            {
                if (CoopGalaxySyncManager.TargetGalaxyId != 0)
                {
                    Traverse.Create(__instance).Property("DestinationGalaxyOverride")?.SetValue(CoopGalaxySyncManager.TargetGalaxyId);
                    Traverse.Create(__instance).Field("<DestinationGalaxyOverride>k__BackingField")?.SetValue(CoopGalaxySyncManager.TargetGalaxyId);
                    GalaxyMapManager.PreserveData = true;
                    Debug.Log($"[DuskersCoopMod] Overriding starting galaxy to match Host Galaxy ID: {CoopGalaxySyncManager.TargetGalaxyId}");
                }
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
                    Debug.LogError($"[DuskersCoopMod] Error in DetermineStartupStarSystem_Postfix: {ex}");
                }
            }
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

        [HarmonyPostfix]
        [HarmonyPatch("BoardCurrentDungeon")]
        public static void BoardCurrentDungeon_Postfix()
        {
            if (IsApplyingRemoteAction) return;
            var net = CoopNetworkManager.Instance;
            if (net != null && net.Role == NetworkRole.Host && net.ConnectedCount > 0)
            {
                // Sync latest save so derelict seed and fleet state match 100%
                CoopSaveSyncManager.SyncToAllClients();

                net.BroadcastPacket(PacketWrapper.Create("STRATEGIC_ACTION", "Host", new StrategicActionData
                {
                    action = "BOARD_DUNGEON"
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
}
