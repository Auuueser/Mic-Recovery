using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace LCMicRecovery
{
    internal static class PluginConfig
    {
        internal static ConfigEntry<bool> EnableMod;
        internal static ConfigEntry<string> LanguageMode;

        internal static ConfigEntry<bool> EnableDebugLog;
        internal static ConfigEntry<bool> EnableStateLogs;
        internal static ConfigEntry<bool> EnableHeartbeatLog;
        internal static ConfigEntry<bool> ShowFiveStepRecoveryLogs;

        internal static ConfigEntry<bool> EnablePreRoundSkipLog;
        internal static ConfigEntry<bool> LogPreRoundSkipOnlyOnce;

        internal static ConfigEntry<bool> SuspendAutoRecoveryWhenNoDevices;
        internal static ConfigEntry<bool> EnableNoDeviceSuspendLog;
        internal static ConfigEntry<bool> LogNoDeviceSuspendOnlyOnce;
        internal static ConfigEntry<bool> AllowManualRecoveryWhenNoDevices;

        internal static ConfigEntry<bool> SuspendAutoRecoveryDuringMenuOrTeardown;
        internal static ConfigEntry<bool> AllowLocalRecoveryWhenGameSideUnsafe;
        internal static ConfigEntry<float> LobbyExitSuspendSeconds;
        internal static ConfigEntry<bool> EnableTeardownSuspendLog;
        internal static ConfigEntry<bool> LogTeardownSuspendOnlyOnce;

        internal static ConfigEntry<bool> EnableManualRecoveryKey;
        internal static ConfigEntry<Key> ManualRecoveryKey;

        internal static ConfigEntry<bool> EnableAutoRecovery;
        internal static ConfigEntry<bool> EnablePreRoundRecovery;
        internal static ConfigEntry<float> AutoCheckIntervalSeconds;
        internal static ConfigEntry<float> RecoveryCooldownSeconds;
        internal static ConfigEntry<float> PostRecoveryGraceSeconds;

        internal static ConfigEntry<string> PreferredDeviceKeywords;
        internal static ConfigEntry<bool> PreferCurrentDeviceIfStillExists;

        internal static ConfigEntry<bool> RecoverWhenMicNameEmpty;
        internal static ConfigEntry<bool> RecoverWhenDeviceMissing;
        internal static ConfigEntry<bool> RecoverWhenUnityNotRecording;

        internal static void Bind(ConfigFile config)
        {
            EnableMod = config.Bind(
                "General",
                "EnableMod",
                true,
                "总开关。关闭后模组不执行任何恢复逻辑。");

            LanguageMode = config.Bind(
                "Localization",
                "LanguageMode",
                "Auto",
                "User-facing language. Auto uses Chinese only when LC-Chinese-Project / V81TestChn is detected; English forces English; Chinese forces Chinese logs but falls HUD back to English when Chinese HUD font support is not detected. / 用户可见语言。Auto 检测到 LC-Chinese-Project / V81TestChn 时使用中文，否则英文；English 强制英文；Chinese 强制中文日志，但未检测到中文 HUD 字体支持时游戏内提示回退英文。");

            EnableDebugLog = config.Bind(
                "Logging",
                "EnableDebugLog",
                false,
                "是否输出调试日志。正常游玩建议关闭。");

            EnableStateLogs = config.Bind(
                "Logging",
                "EnableStateLogs",
                false,
                "是否输出状态检测日志（当前麦克风、IsRecording 等，容易刷屏）。正常游玩建议关闭。");

            EnableHeartbeatLog = config.Bind(
                "Logging",
                "EnableHeartbeatLog",
                false,
                "是否输出 watcher 心跳日志。正常游玩建议关闭。");

            ShowFiveStepRecoveryLogs = config.Bind(
                "Logging",
                "ShowFiveStepRecoveryLogs",
                true,
                "恢复时是否输出 1/5 到 5/5 的明显日志。正常游玩建议开启。");

            EnablePreRoundSkipLog = config.Bind(
                "Logging",
                "EnablePreRoundSkipLog",
                false,
                "在 StartOfRound 尚未就绪时，是否输出“跳过自动恢复检测”的日志。正常游玩建议关闭。");

            LogPreRoundSkipOnlyOnce = config.Bind(
                "Logging",
                "LogPreRoundSkipOnlyOnce",
                true,
                "是否只在每次进入局内前打印一次“StartOfRound 尚未就绪”的日志。");

            SuspendAutoRecoveryWhenNoDevices = config.Bind(
                "No Device Handling",
                "SuspendAutoRecoveryWhenNoDevices",
                true,
                "当没有检测到任何录音设备时，暂停自动恢复，避免反复刷恢复日志。");

            EnableNoDeviceSuspendLog = config.Bind(
                "No Device Handling",
                "EnableNoDeviceSuspendLog",
                false,
                "当没有录音设备时，是否输出“已暂停自动恢复”的提示日志。");

            LogNoDeviceSuspendOnlyOnce = config.Bind(
                "No Device Handling",
                "LogNoDeviceSuspendOnlyOnce",
                true,
                "没有录音设备时，是否只打印一次“已暂停自动恢复”的提示。");

            AllowManualRecoveryWhenNoDevices = config.Bind(
                "No Device Handling",
                "AllowManualRecoveryWhenNoDevices",
                false,
                "当没有录音设备时，是否允许手动按键继续强制恢复。");

            SuspendAutoRecoveryDuringMenuOrTeardown = config.Bind(
                "Teardown Handling",
                "SuspendAutoRecoveryDuringMenuOrTeardown",
                true,
                "当处于主菜单/大厅，或房间正在退出销毁时，暂停自动恢复。建议开启。");

            AllowLocalRecoveryWhenGameSideUnsafe = config.Bind(
                "Teardown Handling",
                "AllowLocalRecoveryWhenGameSideUnsafe",
                true,
                "当游戏侧 ResetDissonanceCommsComponent 不安全时，是否仍允许本地 ResetMicrophoneCapture。建议开启。");

            LobbyExitSuspendSeconds = config.Bind(
                "Teardown Handling",
                "LobbyExitSuspendSeconds",
                8f,
                "退出房间或切换到大厅后，暂停自动恢复的秒数。建议 8。");

            EnableTeardownSuspendLog = config.Bind(
                "Teardown Handling",
                "EnableTeardownSuspendLog",
                false,
                "当因为大厅/退出阶段而暂停自动恢复时，是否输出提示日志。");

            LogTeardownSuspendOnlyOnce = config.Bind(
                "Teardown Handling",
                "LogTeardownSuspendOnlyOnce",
                true,
                "是否只打印一次大厅/退出阶段挂起日志。");

            EnableManualRecoveryKey = config.Bind(
                "Manual Recovery",
                "EnableManualRecoveryKey",
                true,
                "是否启用手动按键恢复。");

            ManualRecoveryKey = config.Bind(
                "Manual Recovery",
                "ManualRecoveryKey",
                Key.F8,
                "手动触发恢复的按键。");

            EnableAutoRecovery = config.Bind(
                "Auto Recovery",
                "EnableAutoRecovery",
                true,
                "是否启用自动恢复。");

            EnablePreRoundRecovery = config.Bind(
                "Auto Recovery",
                "EnablePreRoundRecovery",
                false,
                "是否允许在 StartOfRound 出现前就触发恢复。建议关闭。");

            AutoCheckIntervalSeconds = config.Bind(
                "Auto Recovery",
                "AutoCheckIntervalSeconds",
                3f,
                "自动检测间隔（秒）。");

            RecoveryCooldownSeconds = config.Bind(
                "Auto Recovery",
                "RecoveryCooldownSeconds",
                10f,
                "两次恢复之间的冷却时间（秒）。");

            PostRecoveryGraceSeconds = config.Bind(
                "Auto Recovery",
                "PostRecoveryGraceSeconds",
                4f,
                "恢复完成后的宽限时间（秒）。");

            PreferredDeviceKeywords = config.Bind(
                "Device",
                "PreferredDeviceKeywords",
                "Maxwell,Audeze",
                "优先匹配的设备关键词，多个用英文逗号分隔。");

            PreferCurrentDeviceIfStillExists = config.Bind(
                "Device",
                "PreferCurrentDeviceIfStillExists",
                true,
                "如果当前设备仍存在，恢复时优先保持当前设备。");

            RecoverWhenMicNameEmpty = config.Bind(
                "Recovery Conditions",
                "RecoverWhenMicNameEmpty",
                true,
                "当前麦克风名称为空时是否触发恢复。");

            RecoverWhenDeviceMissing = config.Bind(
                "Recovery Conditions",
                "RecoverWhenDeviceMissing",
                true,
                "当前麦克风不在设备列表中时是否触发恢复。");

            RecoverWhenUnityNotRecording = config.Bind(
                "Recovery Conditions",
                "RecoverWhenUnityNotRecording",
                true,
                "Unity 报告当前麦克风未在录音时是否触发恢复。");
        }

        internal static bool DebugEnabled => EnableDebugLog != null && EnableDebugLog.Value;
        internal static bool StateLogsEnabled => EnableStateLogs != null && EnableStateLogs.Value;
        internal static bool HeartbeatEnabled => EnableHeartbeatLog != null && EnableHeartbeatLog.Value;
    }
}
