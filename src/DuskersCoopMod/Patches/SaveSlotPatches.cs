using System.IO;
using DuskersCoopMod.Save;
using HarmonyLib;

namespace DuskersCoopMod.Patches
{
    [HarmonyPatch(typeof(GameFileHelper))]
    public static class SaveSlotPatches
    {
        [HarmonyPostfix]
        [HarmonyPatch("GetBaseGameFileLocation")]
        public static void GetBaseGameFileLocation_Postfix(ref string __result)
        {
            if (SaveSlotManager.CurrentSlot > 1)
            {
                __result = Path.Combine(__result, "Slot" + SaveSlotManager.CurrentSlot);
            }
        }
    }
}
