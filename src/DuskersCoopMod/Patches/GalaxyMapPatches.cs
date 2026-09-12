using System;
using System.Reflection;
using DuskersCoopMod.Network;
using DuskersCoopMod.Save;
using HarmonyLib;
using UnityEngine;

namespace DuskersCoopMod.Patches
{
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
            }
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
            }
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
            }
        }
    }
}
