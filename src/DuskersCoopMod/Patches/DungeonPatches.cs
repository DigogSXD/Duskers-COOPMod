using System;
using HarmonyLib;
using UnityEngine;

namespace DuskersCoopMod.Patches
{
    [HarmonyPatch(typeof(DungeonManager))]
    public static class DungeonPatches
    {
        public static int SynchronizedDungeonSeed = 0;

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
    }
}
