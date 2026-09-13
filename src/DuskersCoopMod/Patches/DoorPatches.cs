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
            if (IsApplyingDoorSync) return;
            var net = CoopNetworkManager.Instance;
            if (net == null || !net.IsConnected) return;

            string label = !string.IsNullOrEmpty(__instance.LabelSimple) ? __instance.LabelSimple : __instance.Label;
            if (string.IsNullOrEmpty(label)) return;

            if (net.Role == NetworkRole.Host)
            {
                net.BroadcastDoorState(label, true);
            }
            else if (net.Role == NetworkRole.Client)
            {
                net.SendCommand(label);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("CloseDoor")]
        public static void CloseDoor_Postfix(Door __instance)
        {
            if (IsApplyingDoorSync) return;
            var net = CoopNetworkManager.Instance;
            if (net == null || !net.IsConnected) return;

            string label = !string.IsNullOrEmpty(__instance.LabelSimple) ? __instance.LabelSimple : __instance.Label;
            if (string.IsNullOrEmpty(label)) return;

            if (net.Role == NetworkRole.Host)
            {
                net.BroadcastDoorState(label, false);
            }
            else if (net.Role == NetworkRole.Client)
            {
                net.SendCommand(label);
            }
        }
    }
}
