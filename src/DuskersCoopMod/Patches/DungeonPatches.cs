using System;
using HarmonyLib;
using UnityEngine;
using DuskersCoopMod.Network;

namespace DuskersCoopMod.Patches
{
    [HarmonyPatch(typeof(DungeonManager))]
    public static class DungeonPatches
    {
        public static int SynchronizedDungeonSeed = 0;

        // Client: apply the seed received from the Host BEFORE the dungeon generates rooms,
        // so both sides produce identical room names and layouts.
        [HarmonyPrefix]
        [HarmonyPatch("Awake")]
        public static void Awake_Prefix()
        {
            if (SynchronizedDungeonSeed != 0)
            {
                UnityEngine.Random.seed = SynchronizedDungeonSeed;
                Debug.Log($"[DuskersCoopMod] DungeonManager.Awake: Applied synchronized derelict seed: {SynchronizedDungeonSeed}");
            }
        }

        // Host: capture the exact Unity Random seed that the dungeon will use and broadcast it
        // to all connected clients so they generate the same layout.
        // We use a Postfix on Awake because by that point the seed is "locked in" — the dungeon
        // has not yet generated rooms (that happens in Start/Init after Awake), so clients still
        // have time to apply it in their own Awake_Prefix on the same frame.
        [HarmonyPostfix]
        [HarmonyPatch("Awake")]
        public static void Awake_Postfix(DungeonManager __instance)
        {
            try
            {
                var net = CoopNetworkManager.Instance;
                if (net == null || net.Role != NetworkRole.Host || net.ConnectedCount == 0) return;

                // Capture the current seed (this is what the dungeon will use for room generation)
                int currentSeed = UnityEngine.Random.seed;
                if (currentSeed == 0) return;

                // Persist for re-use (e.g. client reconnects mid-dungeon)
                SynchronizedDungeonSeed = currentSeed;

                // Try to get dungeon group key for logging / verification
                string groupKey = "";
                try
                {
                    groupKey = HarmonyLib.Traverse.Create(__instance).Field("groupKey")?.GetValue<string>() ?? "";
                    if (string.IsNullOrEmpty(groupKey))
                        groupKey = HarmonyLib.Traverse.Create(__instance).Property("GroupKey")?.GetValue<string>() ?? "";
                }
                catch { }

                string seedJson = PacketWrapper.Create("DUNGEON_SEED", "Host", new DungeonSeedPacket
                {
                    seed = currentSeed,
                    dungeonGroup = groupKey
                });
                net.BroadcastPacket(seedJson);
                Debug.Log($"[DuskersCoopMod] DUNGEON_SEED broadcast to clients: {currentSeed} (group: {groupKey})");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DuskersCoopMod] DungeonPatches.Awake_Postfix error: {ex.Message}");
            }
        }
    }
}
