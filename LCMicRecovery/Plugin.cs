using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace LCMicRecovery
{
    [BepInPlugin("com.yourname.lcmicrecovery", "LC Mic Recovery", "0.3.6")]
    public class Plugin : BaseUnityPlugin
    {
        internal static Plugin Instance;
        internal static Harmony HarmonyInstance;
        internal static ManualLogSource Log;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            PluginConfig.Bind(Config);

            HarmonyInstance = new Harmony("com.yourname.lcmicrecovery");
            HarmonyInstance.PatchAll();

            Log.LogInfo("LC Mic Recovery loaded.");
            Log.LogInfo("LC Mic Recovery version: 0.3.6");

            var existingWatcher = FindObjectOfType<MicRecoveryWatcher>();
            if (existingWatcher == null)
            {
                var go = new GameObject("LCMicRecovery_Watcher");
                DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                go.AddComponent<MicRecoveryWatcher>();

                Log.LogInfo("LCMicRecovery_Watcher created successfully.");
            }
            else
            {
                Log.LogInfo("LCMicRecovery_Watcher already exists, skipping duplicate creation.");
            }

            Log.LogInfo("Config entries bound. Check LethalConfig in-game to see if they appear.");
        }
    }
}
