using System;
using DuskersCoopMod.Network;
using HarmonyLib;
using UnityEngine;

namespace DuskersCoopMod.Save
{
    public static class CoopGalaxySyncManager
    {
        public static int TargetGalaxyId = 0;
        public static string TargetGalaxyName = "";
        public static int TargetSystemId = 0;
        public static string TargetSystemName = "";
        public static string TargetDockedDungeon = "";
        public static bool HasTargetGalaxyState = false;

        public static void SetShipStarSystem(GalaxyMapManager gmm, StarSystemInfo sys, bool immediate = false)
        {
            if (gmm == null || sys == null) return;
            try
            {
                gmm.SetSelectedStarSystem(sys, false);
                Traverse.Create(gmm).Method("SetPlayerShipStarSystem", new object[] { sys, immediate })?.GetValue();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DuskersCoopMod] Error in SetShipStarSystem: {ex}");
            }
        }

        public static void SetShipDungeon(GalaxyMapManager gmm, DungeonInfo dungeon, bool immediate = false)
        {
            if (gmm == null || dungeon == null) return;
            try
            {
                gmm.SetSelectedDungeon(dungeon, false);
                Traverse.Create(gmm).Method("SetPlayerShipDungeon", new object[] { dungeon, immediate })?.GetValue();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DuskersCoopMod] Error in SetShipDungeon: {ex}");
            }
        }

        public static void BroadcastGalaxyState()
        {
            var net = CoopNetworkManager.Instance;
            if (net == null || net.Role != NetworkRole.Host || net.ConnectedCount == 0) return;
            if (GalaxyMapManager.Instance == null) return;

            try
            {
                var gmm = GalaxyMapManager.Instance;
                var player = GlobalSettings.GameState != null ? GlobalSettings.GameState.ThePlayer : null;
                if (player == null) return;

                int galaxyId = 0;
                string galaxyName = "";
                if (UniverseMapManager.Instance != null && UniverseMapManager.Instance.CurrentUniverseNode != null)
                {
                    galaxyId = UniverseMapManager.Instance.CurrentUniverseNode.InternalID;
                    galaxyName = UniverseMapManager.Instance.CurrentUniverseNode.name;
                }

                int sysId = player.CurrentStarSystem != null ? player.CurrentStarSystem.Id : 0;
                string sysName = player.CurrentStarSystem != null ? player.CurrentStarSystem.Name : "";
                string dungeonName = "";
                if (player.CurrentDockedDungeon != null)
                {
                    dungeonName = !string.IsNullOrEmpty(player.CurrentDockedDungeon.DisplayName)
                        ? player.CurrentDockedDungeon.DisplayName
                        : player.CurrentDockedDungeon.Name;
                }

                var state = new GalaxyStateData
                {
                    galaxyInternalId = galaxyId,
                    galaxyName = galaxyName,
                    starSystemId = sysId,
                    starSystemName = sysName,
                    dockedDungeonName = dungeonName,
                    scrap = player.Inventory != null ? player.Inventory.Scrap : 0,
                    propulsionFuel = player.Inventory != null ? player.Inventory.TotalPropulsionFuel : 0,
                    jumpFuel = player.Inventory != null ? player.Inventory.JumpFuel : 0,
                    mapState = (int)gmm.CurrentMapState
                };

                string packet = PacketWrapper.Create("GALAXY_STATE", "Host", state);
                net.BroadcastPacket(packet);
                Debug.Log($"[DuskersCoopMod] Host Galaxy State broadcasted: Galaxy {galaxyId} ({galaxyName}), System {sysName}, Derelict {dungeonName}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DuskersCoopMod] Error broadcasting GalaxyState: {ex}");
            }
        }

        public static void ApplyGalaxyState(GalaxyStateData state)
        {
            if (state == null) return;

            TargetGalaxyId = state.galaxyInternalId;
            TargetGalaxyName = state.galaxyName;
            TargetSystemId = state.starSystemId;
            TargetSystemName = state.starSystemName;
            TargetDockedDungeon = state.dockedDungeonName;
            HasTargetGalaxyState = true;

            // If client is still in MainMenu / CoopMenuScreen, launch directly into Host's space!
            if (GalaxyMapManager.Instance == null)
            {
                GalaxyMapManager.PreserveData = true;
                if (MainMenu.Instance != null)
                {
                    try
                    {
                        Debug.Log("[DuskersCoopMod] Client received Host Galaxy State in MainMenu. Launching game directly into space!");
                        MainMenu.LaunchGameFinal();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[DuskersCoopMod] Error launching game from GalaxyState: {ex}");
                    }
                }
                return;
            }

            // Client is currently in GalaxyMapManager (space)
            var gmm = GalaxyMapManager.Instance;
            var tr = Traverse.Create(gmm);
            Patches.GalaxyMapPatches.IsApplyingRemoteAction = true;
            try
            {
                var player = GlobalSettings.GameState != null ? GlobalSettings.GameState.ThePlayer : null;
                if (player != null && player.Inventory != null)
                {
                    var invTr = Traverse.Create(player.Inventory);
                    invTr.Property("Scrap")?.SetValue(state.scrap);
                    invTr.Property("JumpFuel")?.SetValue(state.jumpFuel);
                }

                // Align Star System if different
                var currentSys = player != null ? player.CurrentStarSystem : null;
                bool needSysChange = currentSys == null ||
                    (!string.IsNullOrEmpty(state.starSystemName) && !string.Equals(currentSys.Name, state.starSystemName, StringComparison.OrdinalIgnoreCase));

                if (needSysChange && !string.IsNullOrEmpty(state.starSystemName))
                {
                    var nodes = tr.Field("_starSystemNodes").GetValue<System.Collections.IList>();
                    if (nodes != null)
                    {
                        foreach (object node in nodes)
                        {
                            var info = Traverse.Create(node).Property("Info").GetValue<StarSystemInfo>();
                            if (info != null && (string.Equals(info.Name, state.starSystemName, StringComparison.OrdinalIgnoreCase) || (state.starSystemId != 0 && info.Id == state.starSystemId)))
                            {
                                SetShipStarSystem(gmm, info, false);
                                break;
                            }
                        }
                    }
                }

                // Align Docked Dungeon / Derelict if different
                var currentDungeon = player != null ? player.CurrentDockedDungeon : null;
                string currentDungName = currentDungeon != null
                    ? (!string.IsNullOrEmpty(currentDungeon.DisplayName) ? currentDungeon.DisplayName : currentDungeon.Name)
                    : "";

                bool needDungChange = !string.IsNullOrEmpty(state.dockedDungeonName) &&
                    !string.Equals(currentDungName, state.dockedDungeonName, StringComparison.OrdinalIgnoreCase);

                if (needDungChange)
                {
                    StarSystemInfo activeSys = player != null ? player.CurrentStarSystem : null;
                    if (activeSys != null && activeSys.Dungeons != null)
                    {
                        var targetDung = activeSys.Dungeons.Find(d =>
                            (!string.IsNullOrEmpty(d.DisplayName) && d.DisplayName.Equals(state.dockedDungeonName, StringComparison.OrdinalIgnoreCase)) ||
                            (!string.IsNullOrEmpty(d.Name) && d.Name.Equals(state.dockedDungeonName, StringComparison.OrdinalIgnoreCase)) ||
                            d.Id.ToString() == state.dockedDungeonName);

                        if (targetDung != null)
                        {
                            SetShipDungeon(gmm, targetDung, false);
                        }
                    }
                }

                // Sync view state (Star system view vs Galaxy view)
                if (state.mapState == (int)GalaxyMapState.Dungeons && gmm.CurrentMapState != GalaxyMapState.Dungeons)
                {
                    if (player != null && player.CurrentStarSystem != null)
                    {
                        tr.Method("ShowStarSystemView", new object[] { player.CurrentStarSystem, false, true })?.GetValue();
                    }
                }
                else if (state.mapState == (int)GalaxyMapState.StarSystems && gmm.CurrentMapState == GalaxyMapState.Dungeons)
                {
                    tr.Method("HideStarSystemView")?.GetValue();
                }

                tr.Method("UpdateGUIVariables")?.GetValue();
                Debug.Log($"[DuskersCoopMod] Synchronized Space State: System={state.starSystemName}, Derelict={state.dockedDungeonName}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DuskersCoopMod] Error applying GalaxyState: {ex}");
            }
            finally
            {
                Patches.GalaxyMapPatches.IsApplyingRemoteAction = false;
            }
        }
    }
}
