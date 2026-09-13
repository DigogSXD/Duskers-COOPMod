using System;
using HarmonyLib;
using UnityEngine;
using DuskersCoopMod.Network;

namespace DuskersCoopMod.Patches
{
    [HarmonyPatch(typeof(Drone))]
    public static class DronePatches
    {
        [HarmonyPrefix]
        [HarmonyPatch("EnforceNonPenetrationConstraint")]
        public static bool EnforceNonPenetrationConstraint_Prefix(Drone __instance, ref bool __result)
        {
            var net = CoopNetworkManager.Instance;
            if (net != null && net.IsConnected)
            {
                if (net.Role == NetworkRole.Client)
                {
                    // If this drone is not locally steered by this client,
                    // do not let client-side collider penetration fight the host's authoritative position!
                    bool isLocalSteer = CoopNetworkManager.IsWindowFocused && (Time.time - net.LastClientSteeringTime < 0.15f) && (__instance.DroneNumber == net.LastClientSteeringDrone);
                    if (!isLocalSteer)
                    {
                        __result = false;
                        return false; // Skip penetration constraint!
                    }
                }
            }
            return true;
        }
    }
}
