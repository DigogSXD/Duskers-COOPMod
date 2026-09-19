using System;
using HarmonyLib;
using UnityEngine;
using DuskersCoopMod.Network;

namespace DuskersCoopMod.Patches
{
    [HarmonyPatch(typeof(Door))]
    public static class DoorPatches
    {
        public static bool IsApplyingDoorSync = false;

        [HarmonyPostfix]
        [HarmonyPatch("open", new Type[] { typeof(bool), typeof(bool) })]
        public static void Open_Postfix(Door __instance, bool __result)
        {
            // NOTE: IsExecutingRemoteCommand guard removed intentionally — the Host must always
            // broadcast the authoritative door state, including when executing a client's command.
            if (IsApplyingDoorSync) return;
            var net = CoopNetworkManager.Instance;
            if (net == null || !net.IsConnected) return;

            string label = !string.IsNullOrEmpty(__instance.LabelSimple) ? __instance.LabelSimple : __instance.Label;
            if (string.IsNullOrEmpty(label)) return;

            if (net.Role == NetworkRole.Host)
            {
                net.BroadcastDoorState(label, true);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("CloseDoor")]
        public static void CloseDoor_Postfix(Door __instance)
        {
            // NOTE: IsExecutingRemoteCommand guard removed intentionally — the Host must always
            // broadcast the authoritative door state, including when executing a client's command.
            if (IsApplyingDoorSync) return;
            var net = CoopNetworkManager.Instance;
            if (net == null || !net.IsConnected) return;

            string label = !string.IsNullOrEmpty(__instance.LabelSimple) ? __instance.LabelSimple : __instance.Label;
            if (string.IsNullOrEmpty(label)) return;

            if (net.Role == NetworkRole.Host)
            {
                net.BroadcastDoorState(label, false);
            }
        }
    }
}
