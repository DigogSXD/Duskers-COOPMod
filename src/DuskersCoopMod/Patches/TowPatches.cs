using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using DuskersCoopMod.Network;

namespace DuskersCoopMod.Patches
{
    /// <summary>
    /// Patches for drone tow (carry) mechanics.
    /// Applied manually at startup to avoid crashes when method names differ between
    /// Duskers builds. We try several candidate method names.
    /// </summary>
    public static class TowPatches
    {
        public static bool IsApplyingRemoteTow = false;

        // Called by CoopPlugin after _harmony.PatchAll() to apply tow-specific patches safely.
        public static void ApplyPatches(Harmony harmony)
        {
            var droneType = typeof(Drone);

            // --- Tow start candidates ---
            TryPatchPostfix(harmony, droneType, "Tow",      new[] { typeof(Drone) }, nameof(OnTowStarted));
            TryPatchPostfix(harmony, droneType, "StartTow", new[] { typeof(Drone) }, nameof(OnTowStarted));
            TryPatchPostfix(harmony, droneType, "carry",    new[] { typeof(Drone) }, nameof(OnTowStarted));
            TryPatchPostfix(harmony, droneType, "Carry",    new[] { typeof(Drone) }, nameof(OnTowStarted));

            // --- Tow release candidates ---
            TryPatchPostfix(harmony, droneType, "ReleaseTow", Type.EmptyTypes, nameof(OnTowReleased));
            TryPatchPostfix(harmony, droneType, "StopTow",    Type.EmptyTypes, nameof(OnTowReleased));
            TryPatchPostfix(harmony, droneType, "release",    Type.EmptyTypes, nameof(OnTowReleased));
            TryPatchPostfix(harmony, droneType, "Release",    Type.EmptyTypes, nameof(OnTowReleased));
        }

        private static void TryPatchPostfix(Harmony harmony, Type type, string methodName, Type[] args, string postfixName)
        {
            try
            {
                var original = args.Length > 0
                    ? type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, args, null)
                    : type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (original == null) return;

                var postfix = new HarmonyMethod(typeof(TowPatches).GetMethod(postfixName,
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));

                harmony.Patch(original, postfix: postfix);
                Debug.Log($"[DuskersCoopMod] TowPatches: patched {type.Name}.{methodName}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DuskersCoopMod] TowPatches: could not patch {type.Name}.{methodName}: {ex.Message}");
            }
        }

        // Postfix for any tow-start method: (Drone __instance, Drone other)
        public static void OnTowStarted(Drone __instance, Drone other)
        {
            if (IsApplyingRemoteTow || __instance == null || other == null) return;
            var net = CoopNetworkManager.Instance;
            if (net == null || !net.IsConnected) return;

            var packet = new DroneTowSyncPacket
            {
                towerDroneNumber = __instance.DroneNumber,
                towedDroneNumber = other.DroneNumber,
                isTowing = true
            };

            try
            {
                if (net.Role == NetworkRole.Host)
                    net.BroadcastPacket(PacketWrapper.Create("DRONE_TOW_SYNC", "Host", packet));
                else if (net.Role == NetworkRole.Client)
                    net.SendPacketToHost(PacketWrapper.Create("CLIENT_DRONE_TOW", "Operator", packet));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DuskersCoopMod] TowPatches.OnTowStarted error: {ex.Message}");
            }
        }

        // Postfix for any tow-release method: (Drone __instance)
        public static void OnTowReleased(Drone __instance)
        {
            if (IsApplyingRemoteTow || __instance == null) return;
            var net = CoopNetworkManager.Instance;
            if (net == null || !net.IsConnected) return;

            var packet = new DroneTowSyncPacket
            {
                towerDroneNumber = __instance.DroneNumber,
                towedDroneNumber = -1,
                isTowing = false
            };

            try
            {
                if (net.Role == NetworkRole.Host)
                    net.BroadcastPacket(PacketWrapper.Create("DRONE_TOW_SYNC", "Host", packet));
                else if (net.Role == NetworkRole.Client)
                    net.SendPacketToHost(PacketWrapper.Create("CLIENT_DRONE_TOW", "Operator", packet));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DuskersCoopMod] TowPatches.OnTowReleased error: {ex.Message}");
            }
        }
    }
}
