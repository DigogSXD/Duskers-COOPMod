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
            if (net != null && net.IsConnected && net.Role == NetworkRole.Client)
            {
                bool isLocalSteer = (__instance == DroneManager.Instance?.CurrentDrone) && CoopNetworkManager.IsSteeringInputActive();
                if (!isLocalSteer)
                {
                    __result = false;
                    return false; // Skip penetration constraint!
                }
            }
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch("IsInOuterSpace")]
        public static bool IsInOuterSpace_Prefix(Drone __instance, ref bool __result)
        {
            var net = CoopNetworkManager.Instance;
            if (net != null && net.IsConnected && net.Role == NetworkRole.Client)
            {
                bool isLocalSteer = (__instance == DroneManager.Instance?.CurrentDrone) && CoopNetworkManager.IsSteeringInputActive();
                if (!isLocalSteer)
                {
                    __result = false;
                    return false; // Skip outer space rollback on client for remote drones!
                }
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(DroneManager))]
    public static class DroneManagerViewPatches
    {
        [HarmonyPostfix]
        [HarmonyPatch("switchCameraView")]
        public static void SwitchCameraView_Postfix()
        {
            if (DroneManager.Instance != null && DroneManager.Instance.dronesList != null)
            {
                foreach (var d in DroneManager.Instance.dronesList)
                {
                    CoopNetworkManager.SyncDroneVisualHierarchy(d);
                }
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("SetDroneNumber", new Type[] { typeof(int) })]
        public static void SetDroneNumber_Postfix(int droneNumber)
        {
            if (DroneManager.Instance != null && DroneManager.Instance.CurrentDrone != null)
            {
                CoopNetworkManager.SyncDroneVisualHierarchy(DroneManager.Instance.CurrentDrone);
                if (GlobalSettings.cameraMode == CameraMode.Drone)
                {
                    DroneManager.Instance.positionDroneCamera();
                }
            }
        }
    }
}
