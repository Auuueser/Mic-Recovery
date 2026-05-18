using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace LCMicRecovery
{
    [BepInPlugin("com.yourname.lcmicrecovery", "LC Mic Recovery", "0.3.7")]
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

            Log.LogInfo(MicRecoveryText.T(
                "LC Mic Recovery 已加载。",
                "LC Mic Recovery loaded."));
            Log.LogInfo(MicRecoveryText.T(
                "LC Mic Recovery version: 0.3.7",
                "LC Mic Recovery version: 0.3.7"));

            var existingWatcher = FindObjectOfType<MicRecoveryWatcher>();
            if (existingWatcher == null)
            {
                var go = new GameObject("LCMicRecovery_Watcher");
                DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                go.AddComponent<MicRecoveryWatcher>();

                Log.LogInfo(MicRecoveryText.T(
                    "LCMicRecovery_Watcher 已创建。",
                    "LCMicRecovery_Watcher created successfully."));
            }
            else
            {
                Log.LogInfo(MicRecoveryText.T(
                    "LCMicRecovery_Watcher 已存在，跳过重复创建。",
                    "LCMicRecovery_Watcher already exists, skipping duplicate creation."));
            }

            Log.LogInfo(MicRecoveryText.T(
                "配置项已绑定。可在游戏内 LethalConfig 检查是否显示。",
                "Config entries bound. Check LethalConfig in-game to see if they appear."));
        }
    }
}
