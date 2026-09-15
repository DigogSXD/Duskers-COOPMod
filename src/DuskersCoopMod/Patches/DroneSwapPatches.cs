using System;
using HarmonyLib;
using UnityEngine;
using DuskersCoopMod.Network;

namespace DuskersCoopMod.Patches
{
    [HarmonyPatch(typeof(DroneSwapUi2))]
    public static class DroneSwapPatches
    {
        public static bool IsApplyingRemoteSwap = false;

        [HarmonyPostfix]
        [HarmonyPatch("SwapSpecifiedSlots")]
        public static void SwapSpecifiedSlots_Postfix(DroneSwapUi2 __instance, int leftSlotNum, int rightSlotNum)
        {
            if (IsApplyingRemoteSwap) return;
            var net = CoopNetworkManager.Instance;
            if (net == null || !net.IsConnected) return;

            try
            {
                var panels = Traverse.Create(__instance).Field("_dronePanels").GetValue<DroneSwapDroneInfoPanel[]>();
                if (panels != null && panels.Length >= 2 && panels[0].Drone != null && panels[1].Drone != null)
                {
                    var packet = new SwapUpgradesPacket
                    {
                        droneA = panels[0].Drone.DroneNumber,
                        slotA = leftSlotNum,
                        droneB = panels[1].Drone.DroneNumber,
                        slotB = rightSlotNum
                    };

                    if (net.Role == NetworkRole.Host)
                    {
                        net.BroadcastPacket(PacketWrapper.Create("SWAP_UPGRADES", "Host", packet));
                    }
                    else if (net.Role == NetworkRole.Client)
                    {
                        net.SendPacketToHost(PacketWrapper.Create("CLIENT_SWAP_UPGRADES", "Operator", packet));
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DuskersCoopMod] Error in SwapSpecifiedSlots_Postfix: {ex}");
            }
        }
    }
}
