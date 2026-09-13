using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using DuskersCoopMod.Network;

namespace DuskersCoopMod
{
    [BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
    public class CoopPlugin : BaseUnityPlugin
    {
        public const string PLUGIN_GUID = "com.diogo.duskerscoop";
        public const string PLUGIN_NAME = "Duskers Coop Mod";
        public const string PLUGIN_VERSION = "1.1.4";

        public static ConfigEntry<int> ConfigPort;

        private Harmony _harmony;

        private void Awake()
        {
            Logger.LogInfo($"{PLUGIN_NAME} v{PLUGIN_VERSION} initializing...");

            try
            {
                ConfigPort = Config.Bind("Network", "ServerPort", 7777, "The TCP port used for hosting multiplayer sessions (1024-65535)");
                _harmony = new Harmony(PLUGIN_GUID);
                _harmony.PatchAll();
                Logger.LogInfo("Harmony patches applied successfully.");

                GameObject netGo = new GameObject("DuskersCoopNetworkManager");
                netGo.AddComponent<CoopNetworkManager>();
                DontDestroyOnLoad(netGo);

                GameObject steamGo = new GameObject("DuskersCoopSteamManager");
                steamGo.AddComponent<SteamCoopManager>();
                DontDestroyOnLoad(steamGo);

                Logger.LogInfo($"{PLUGIN_NAME} ready. Type 'coop help' in terminal.");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Error loading {PLUGIN_NAME}: {ex}");
            }
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
