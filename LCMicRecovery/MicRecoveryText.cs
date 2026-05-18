using System;
using System.Linq;
using BepInEx.Bootstrap;

namespace LCMicRecovery
{
    internal static class MicRecoveryText
    {
        private const string ChineseProjectGuid = "cn.codex.v81testchn";
        private const string ChineseProjectAssemblyName = "V81TestChn";

        private static bool _hudFallbackWarningLogged;

        internal static string T(string chinese, string english)
        {
            return ShouldUseChineseText() ? chinese : english;
        }

        internal static string Format(string chinese, string english, params object[] args)
        {
            return string.Format(T(chinese, english), args);
        }

        internal static string RecoveryCompleteTipTitle
        {
            get { return ShouldUseChineseHudText() ? "麦克风修复已触发" : "Mic recovery triggered"; }
        }

        internal static string RecoveryCompleteTipBody
        {
            get
            {
                return ShouldUseChineseHudText()
                    ? "请测试语音。若仍异常，请查看日志。"
                    : "Test your voice. If it still fails, check logs.";
            }
        }

        internal static void WarnHudFallbackIfNeeded()
        {
            if (_hudFallbackWarningLogged || !IsForcedChineseMode() || IsChineseHudSupported())
                return;

            _hudFallbackWarningLogged = true;
            Plugin.Log?.LogWarning("[MicRecovery] LanguageMode=Chinese，但未检测到 LC-Chinese-Project / V81TestChn；游戏内 HUD 提示已回退英文，以避免中文方块乱码。BepInEx 日志仍使用中文。");
        }

        private static bool ShouldUseChineseHudText()
        {
            return ShouldUseChineseText() && IsChineseHudSupported();
        }

        private static bool ShouldUseChineseText()
        {
            string mode = GetLanguageMode();
            if (IsEnglishMode(mode))
                return false;

            if (IsForcedChineseMode(mode))
                return true;

            return IsChineseProjectLoaded();
        }

        private static bool IsForcedChineseMode()
        {
            return IsForcedChineseMode(GetLanguageMode());
        }

        private static bool IsForcedChineseMode(string mode)
        {
            return string.Equals(mode, "Chinese", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(mode, "zh", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(mode, "zh-CN", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(mode, "zh-Hans", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsEnglishMode(string mode)
        {
            return string.Equals(mode, "English", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(mode, "en", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(mode, "en-US", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetLanguageMode()
        {
            string mode = PluginConfig.LanguageMode != null ? PluginConfig.LanguageMode.Value : null;
            return string.IsNullOrWhiteSpace(mode) ? "Auto" : mode.Trim();
        }

        private static bool IsChineseHudSupported()
        {
            return IsChineseProjectLoaded();
        }

        private static bool IsChineseProjectLoaded()
        {
            try
            {
                if (Chainloader.PluginInfos != null)
                {
                    if (Chainloader.PluginInfos.ContainsKey(ChineseProjectGuid))
                        return true;

                    foreach (var plugin in Chainloader.PluginInfos.Values)
                    {
                        var metadata = plugin?.Metadata;
                        if (metadata == null)
                            continue;

                        if (string.Equals(metadata.GUID, ChineseProjectGuid, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(metadata.Name, ChineseProjectAssemblyName, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }
            catch
            {
            }

            try
            {
                return AppDomain.CurrentDomain.GetAssemblies().Any(assembly =>
                    string.Equals(assembly.GetName().Name, ChineseProjectAssemblyName, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }
    }
}
