using System;
using System.Collections.Generic;
using System.Linq;
using Dissonance;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LCMicRecovery
{
    internal static class MicRecoveryCore
    {
        private enum GameSideResetResult
        {
            Invoked,
            SkippedInFlight,
            SkippedUnsafe,
            MethodNotFound,
            Failed
        }

        private const float FailedRecoveryBackoffSeconds = 3f;
        private const float FailedRecoveryLogBackoffSeconds = 10f;
        private const float GameSideResetGraceSeconds = 3f;

        private static float _lastRecoveryTime = -999f;
        private static float _lastFailedRecoveryTime = -999f;
        private static float _lastFailureLogTime = -999f;
        private static float _postRecoveryGraceUntil = -999f;
        private static bool _gameSideResetCompatibilityWarningLogged;
        private static bool _gameSideResetCoroutineWarningLogged;
        private static bool _gameSideResetInFlight;

        internal static bool IsInPostRecoveryGracePeriod
        {
            get { return Time.unscaledTime < _postRecoveryGraceUntil; }
        }

        internal static bool TryRecover(string reason)
        {
            return TryRecover(reason, false);
        }

        internal static bool TryRecover(string reason, bool ignoreCooldown)
        {
            float cooldown = PluginConfig.RecoveryCooldownSeconds != null
                ? Mathf.Max(0f, PluginConfig.RecoveryCooldownSeconds.Value)
                : 10f;

            if (!ignoreCooldown && Time.unscaledTime - _lastRecoveryTime < cooldown)
            {
                if (PluginConfig.DebugEnabled)
                    Plugin.Log?.LogWarning(MicRecoveryText.Format(
                        "[MicRecovery] 恢复冷却中，跳过本次恢复。reason={0}",
                        "[MicRecovery] Recovery cooldown active; skipping this recovery. reason={0}",
                        reason));
                return false;
            }

            if (!ignoreCooldown && Time.unscaledTime - _lastFailedRecoveryTime < FailedRecoveryBackoffSeconds)
            {
                LogFailedRecoveryBackoff(reason);
                return false;
            }

            LogRecoveryStep(1, MicRecoveryText.T(
                "检测到疑似麦克风失效，开始执行恢复",
                "Possible microphone failure detected; starting recovery"));
            LogRecoveryStep(2, MicRecoveryText.Format(
                "触发原因：{0}",
                "Trigger reason: {0}",
                reason));

            try
            {
                var comms = UnityEngine.Object.FindObjectOfType<DissonanceComms>();
                if (comms == null)
                {
                    MarkRecoveryFailed(reason, MicRecoveryText.T("未找到 DissonanceComms", "DissonanceComms not found"), () =>
                    {
                        Plugin.Log?.LogError(MicRecoveryText.T(
                            "========== [麦克风修复 3/5] 未找到 DissonanceComms，无法执行恢复 ==========",
                            "========== [Mic Recovery 3/5] DissonanceComms was not found; recovery cannot run =========="));
                        Plugin.Log?.LogError(MicRecoveryText.T(
                            "========== [麦克风修复 4/5] 本次恢复失败 ==========",
                            "========== [Mic Recovery 4/5] This recovery attempt failed =========="));
                        Plugin.Log?.LogError(MicRecoveryText.T(
                            "========== [麦克风修复 5/5] 请检查当前场景是否已进入可语音状态 ==========",
                            "========== [Mic Recovery 5/5] Check whether the current scene is ready for voice chat =========="));
                    });
                    return false;
                }

                string currentMicName = null;
                try
                {
                    currentMicName = comms.MicrophoneName;
                }
                catch (Exception ex)
                {
                    if (PluginConfig.DebugEnabled)
                        Plugin.Log?.LogWarning(MicRecoveryText.Format(
                            "[MicRecovery] 读取当前麦克风名称失败：{0}",
                            "[MicRecovery] Failed to read current microphone name: {0}",
                            ex.Message));
                }

                var deviceList = new List<string>();
                try
                {
                    comms.GetMicrophoneDevices(deviceList);
                }
                catch (Exception ex)
                {
                    if (PluginConfig.DebugEnabled)
                        Plugin.Log?.LogWarning(MicRecoveryText.Format(
                            "[MicRecovery] 获取麦克风列表失败：{0}",
                            "[MicRecovery] Failed to get microphone device list: {0}",
                            ex.Message));
                }

                string[] devices = deviceList.ToArray();
                LogRecoveryStep(3, MicRecoveryText.Format(
                    "当前麦克风：{0} | 设备数：{1}",
                    "Current microphone: {0} | Device count: {1}",
                    string.IsNullOrEmpty(currentMicName) ? MicRecoveryText.T("<空>", "<empty>") : currentMicName,
                    devices.Length));

                string preferred = PickPreferredDevice(devices, currentMicName);
                if (!string.IsNullOrEmpty(preferred))
                {
                    if (!string.Equals(currentMicName, preferred, StringComparison.Ordinal))
                    {
                        comms.MicrophoneName = preferred;
                        if (PluginConfig.DebugEnabled)
                            Plugin.Log?.LogInfo(MicRecoveryText.Format(
                                "[MicRecovery] 已切换麦克风到首选设备：{0}",
                                "[MicRecovery] Switched microphone to preferred device: {0}",
                                preferred));
                    }
                    else
                    {
                        if (PluginConfig.DebugEnabled)
                            Plugin.Log?.LogInfo(MicRecoveryText.Format(
                                "[MicRecovery] 当前已是首选设备：{0}",
                                "[MicRecovery] Current microphone is already the preferred device: {0}",
                                preferred));
                    }
                }
                else
                {
                    Plugin.Log?.LogWarning(MicRecoveryText.T(
                        "[MicRecovery] 没找到可用的首选设备，将沿用当前设备继续恢复。",
                        "[MicRecovery] No preferred device was found; continuing recovery with the current device."));
                }

                comms.ResetMicrophoneCapture();
                if (PluginConfig.DebugEnabled || PluginConfig.StateLogsEnabled)
                    Plugin.Log?.LogInfo(MicRecoveryText.T(
                        "[MicRecovery] 本地 ResetMicrophoneCapture 已成功执行。",
                        "[MicRecovery] Local ResetMicrophoneCapture completed successfully."));

                _lastRecoveryTime = Time.unscaledTime;
                _lastFailedRecoveryTime = -999f;

                // 只有在游戏侧上下文明确安全时，才调用游戏自己的重置入口
                GameSideResetResult gameSideResetResult = TryInvokeGameSideReset();
                if (PluginConfig.DebugEnabled || PluginConfig.StateLogsEnabled)
                    Plugin.Log?.LogInfo($"[MicRecovery] Game side reset result: {gameSideResetResult}");

                float grace = PluginConfig.PostRecoveryGraceSeconds != null
                    ? Mathf.Max(0f, PluginConfig.PostRecoveryGraceSeconds.Value)
                    : 4f;
                if (gameSideResetResult == GameSideResetResult.Invoked)
                    grace += GameSideResetGraceSeconds;
                _postRecoveryGraceUntil = Mathf.Max(_postRecoveryGraceUntil, Time.unscaledTime + grace);
                float effectiveGrace = Mathf.Max(0f, _postRecoveryGraceUntil - Time.unscaledTime);

                switch (gameSideResetResult)
                {
                    case GameSideResetResult.Invoked:
                        LogRecoveryStep(4, MicRecoveryText.T(
                            "已执行 ResetMicrophoneCapture，已启动游戏侧重置",
                            "ResetMicrophoneCapture completed; game-side reset started"));
                        break;
                    case GameSideResetResult.SkippedInFlight:
                        LogRecoveryStep(4, MicRecoveryText.T(
                            "已执行 ResetMicrophoneCapture，已跳过游戏侧重置（已有重置正在执行）",
                            "ResetMicrophoneCapture completed; skipped game-side reset because one is already running"));
                        break;
                    case GameSideResetResult.SkippedUnsafe:
                        LogRecoveryStep(4, MicRecoveryText.T(
                            "已执行 ResetMicrophoneCapture，已跳过游戏侧重置（当前阶段不安全）",
                            "ResetMicrophoneCapture completed; skipped game-side reset because the current phase is unsafe"));
                        break;
                    case GameSideResetResult.MethodNotFound:
                        LogRecoveryStep(4, MicRecoveryText.T(
                            "已执行 ResetMicrophoneCapture，未执行游戏侧重置（未找到重置方法）",
                            "ResetMicrophoneCapture completed; game-side reset was not run because the reset method was not found"));
                        break;
                    default:
                        LogRecoveryStep(4, MicRecoveryText.T(
                            "已执行 ResetMicrophoneCapture，未执行游戏侧重置（调用失败）",
                            "ResetMicrophoneCapture completed; game-side reset was not run because the call failed"));
                        break;
                }
                LogRecoveryStep(5, MicRecoveryText.Format(
                    "麦克风恢复流程已触发，进入 {0:0.##} 秒恢复宽限期，请立刻测试语音是否恢复",
                    "Microphone recovery has been triggered; entering a {0:0.##} second grace period. Test voice chat now.",
                    effectiveGrace));
                ShowRecoveryCompleteTip();
                LogRecoveryCompletionDetail();

                return true;
            }
            catch (Exception ex)
            {
                MarkRecoveryFailed(reason, MicRecoveryText.T("恢复过程中出现异常", "An exception occurred during recovery"), () =>
                {
                    Plugin.Log?.LogError(MicRecoveryText.T(
                        "========== [麦克风修复 3/5] 恢复过程中出现异常 ==========",
                        "========== [Mic Recovery 3/5] An exception occurred during recovery =========="));
                    Plugin.Log?.LogError(MicRecoveryText.Format(
                        "========== [麦克风修复 4/5] 异常：{0} ==========",
                        "========== [Mic Recovery 4/5] Exception: {0} ==========",
                        ex));
                    Plugin.Log?.LogError(MicRecoveryText.T(
                        "========== [麦克风修复 5/5] 本次恢复失败，请把日志贴回来 ==========",
                        "========== [Mic Recovery 5/5] This recovery attempt failed; please provide the log =========="));
                });
                return false;
            }
        }

        internal static bool IsMenuSceneActive()
        {
            try
            {
                Scene scene = SceneManager.GetActiveScene();
                if (!scene.IsValid())
                    return true;

                string name = scene.name ?? string.Empty;
                return name.IndexOf("menu", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return true;
            }
        }

        internal static bool IsRecoveryBlockedByScene()
        {
            return IsMenuSceneActive();
        }

        private static bool TryGetStartOfRoundInstance(out StartOfRound startOfRoundInstance)
        {
            startOfRoundInstance = null;

            try
            {
                startOfRoundInstance = StartOfRound.Instance;
            }
            catch
            {
                startOfRoundInstance = null;
                return false;
            }

            return startOfRoundInstance != null;
        }

        internal static bool IsStartOfRoundReady()
        {
            return TryGetStartOfRoundInstance(out _);
        }

        internal static bool IsGameSideResetSafe()
        {
            try
            {
                if (IsMenuSceneActive())
                    return false;

                if (!TryGetStartOfRoundInstance(out StartOfRound startOfRoundInstance))
                    return false;

                if (startOfRoundInstance.localPlayerController == null)
                    return false;

                if (startOfRoundInstance.voiceChatModule == null)
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void LogRecoveryStep(int step, string message)
        {
            bool showFive = PluginConfig.ShowFiveStepRecoveryLogs != null && PluginConfig.ShowFiveStepRecoveryLogs.Value;
            if (showFive)
                Plugin.Log?.LogWarning(MicRecoveryText.Format(
                    "========== [麦克风修复 {0}/5] {1} ==========",
                    "========== [Mic Recovery {0}/5] {1} ==========",
                    step,
                    message));
            else if (PluginConfig.DebugEnabled)
                Plugin.Log?.LogInfo($"[MicRecovery] Step {step}/5: {message}");
        }

        private static void ShowRecoveryCompleteTip()
        {
            try
            {
                if (HUDManager.Instance == null)
                    return;

                HUDManager.Instance.DisplayTip(
                    MicRecoveryText.RecoveryCompleteTipTitle,
                    MicRecoveryText.RecoveryCompleteTipBody,
                    false,
                    false,
                    "LCMicRecovery_RecoveryComplete");
                MicRecoveryText.WarnHudFallbackIfNeeded();
            }
            catch (Exception ex)
            {
                if (PluginConfig.DebugEnabled)
                    Plugin.Log?.LogWarning(MicRecoveryText.Format(
                        "[MicRecovery] 显示恢复完成提示失败：{0}",
                        "[MicRecovery] Failed to show recovery completion tip: {0}",
                        ex.Message));
            }
        }

        private static void LogRecoveryCompletionDetail()
        {
            bool showFive = PluginConfig.ShowFiveStepRecoveryLogs != null && PluginConfig.ShowFiveStepRecoveryLogs.Value;
            if (!showFive && !PluginConfig.DebugEnabled && !PluginConfig.StateLogsEnabled)
                return;

            Plugin.Log?.LogInfo(MicRecoveryText.T(
                "[MicRecovery] 修复完成说明：修复完成以当前可检测到的实际修复效果为准。若修复后问题仍未解决，可能存在其他影响因素，需结合具体场景进一步排查。",
                "[MicRecovery] Recovery completion note: Completion is based on the currently detectable recovery result. If the issue remains, other factors may be involved and the specific situation should be checked."));
        }

        private static void MarkRecoveryFailed(string reason, string summary, Action writeFailureLog)
        {
            _lastFailedRecoveryTime = Time.unscaledTime;

            if (ShouldWriteFailureLog())
            {
                writeFailureLog?.Invoke();
                return;
            }

            if (PluginConfig.DebugEnabled)
                Plugin.Log?.LogInfo(MicRecoveryText.Format(
                    "[MicRecovery] 本次恢复失败，失败日志退避中：{0}。reason={1}",
                    "[MicRecovery] This recovery attempt failed; failure log backoff is active: {0}. reason={1}",
                    summary,
                    reason));
        }

        private static bool ShouldWriteFailureLog()
        {
            if (Time.unscaledTime - _lastFailureLogTime < FailedRecoveryLogBackoffSeconds)
                return false;

            _lastFailureLogTime = Time.unscaledTime;
            return true;
        }

        private static void LogFailedRecoveryBackoff(string reason)
        {
            if (!ShouldWriteFailureLog())
                return;

            float remaining = Mathf.Max(0f, FailedRecoveryBackoffSeconds - (Time.unscaledTime - _lastFailedRecoveryTime));
            Plugin.Log?.LogWarning(MicRecoveryText.Format(
                "[MicRecovery] 最近一次恢复失败，进入失败退避，{0:0.##} 秒后再尝试恢复。reason={1}",
                "[MicRecovery] The previous recovery attempt failed; backing off for {0:0.##} more seconds. reason={1}",
                remaining,
                reason));
        }

        private static string PickPreferredDevice(string[] devices, string currentMicName)
        {
            if (devices == null || devices.Length == 0)
                return null;

            if (PluginConfig.PreferCurrentDeviceIfStillExists != null &&
                PluginConfig.PreferCurrentDeviceIfStillExists.Value &&
                !string.IsNullOrWhiteSpace(currentMicName) &&
                devices.Any(d => string.Equals(d, currentMicName, StringComparison.Ordinal)))
            {
                return currentMicName;
            }

            string raw = PluginConfig.PreferredDeviceKeywords != null
                ? PluginConfig.PreferredDeviceKeywords.Value
                : "Maxwell,Audeze";

            string[] keywords = raw
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();

            foreach (string keyword in keywords)
            {
                var match = devices.FirstOrDefault(d =>
                    !string.IsNullOrEmpty(d) &&
                    d.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);

                if (!string.IsNullOrEmpty(match))
                    return match;
            }

            return devices.FirstOrDefault(d => !string.IsNullOrWhiteSpace(d));
        }

        private static GameSideResetResult TryInvokeGameSideReset()
        {
            try
            {
                if (!IsGameSideResetSafe())
                {
                    if (PluginConfig.DebugEnabled)
                        Plugin.Log?.LogInfo(MicRecoveryText.T(
                            "[MicRecovery] 当前不适合调用游戏侧重置入口，已跳过 ResetDissonanceCommsComponent。",
                            "[MicRecovery] Current state is unsafe for the game-side reset entry; skipped ResetDissonanceCommsComponent."));
                    return GameSideResetResult.SkippedUnsafe;
                }

                if (!TryGetStartOfRoundInstance(out StartOfRound startOfRoundInstance))
                {
                    LogGameSideResetCompatibilityWarningOnce(MicRecoveryText.T("无法获取 StartOfRound 实例", "StartOfRound instance is unavailable"));
                    return GameSideResetResult.SkippedUnsafe;
                }

                if (startOfRoundInstance.voiceChatModule == null)
                {
                    LogGameSideResetCompatibilityWarningOnce(MicRecoveryText.T("StartOfRound.voiceChatModule 不可用", "StartOfRound.voiceChatModule is unavailable"));
                    if (PluginConfig.DebugEnabled)
                        Plugin.Log?.LogWarning(MicRecoveryText.T(
                            "[MicRecovery] StartOfRound.voiceChatModule 不可用。",
                            "[MicRecovery] StartOfRound.voiceChatModule is unavailable."));
                    return GameSideResetResult.SkippedUnsafe;
                }

                if (Plugin.Instance == null)
                {
                    if (PluginConfig.DebugEnabled)
                        Plugin.Log?.LogWarning(MicRecoveryText.T(
                            "[MicRecovery] Plugin.Instance 不可用，无法启动游戏侧重置协程。",
                            "[MicRecovery] Plugin.Instance is unavailable; cannot start the game-side reset coroutine."));
                    return GameSideResetResult.Failed;
                }

                if (_gameSideResetInFlight)
                {
                    if (PluginConfig.DebugEnabled)
                        Plugin.Log?.LogInfo(MicRecoveryText.T(
                            "[MicRecovery] 游戏侧 ResetDissonanceCommsComponent 已在执行中，跳过新的游戏侧重置。",
                            "[MicRecovery] Game-side ResetDissonanceCommsComponent is already running; skipped a new game-side reset."));
                    return GameSideResetResult.SkippedInFlight;
                }

                var routine = startOfRoundInstance.ResetDissonanceCommsComponent();
                if (routine == null)
                {
                    if (PluginConfig.DebugEnabled)
                        Plugin.Log?.LogWarning(MicRecoveryText.T(
                            "[MicRecovery] 游戏侧 ResetDissonanceCommsComponent 未返回可执行协程。",
                            "[MicRecovery] Game-side ResetDissonanceCommsComponent did not return a runnable coroutine."));
                    return GameSideResetResult.Failed;
                }

                _gameSideResetInFlight = true;
                try
                {
                    Plugin.Instance.StartCoroutine(SafeRunGameSideResetCoroutine(routine));
                }
                catch
                {
                    _gameSideResetInFlight = false;
                    throw;
                }

                return GameSideResetResult.Invoked;
            }
            catch (MissingMethodException ex)
            {
                LogGameSideResetCompatibilityWarningOnce(MicRecoveryText.T("ResetDissonanceCommsComponent 运行时不可用", "ResetDissonanceCommsComponent is unavailable at runtime"));
                if (PluginConfig.DebugEnabled)
                    Plugin.Log?.LogWarning(MicRecoveryText.Format(
                        "[MicRecovery] 游戏侧重置入口运行时不可用：{0}",
                        "[MicRecovery] Game-side reset entry is unavailable at runtime: {0}",
                        ex.Message));
                return GameSideResetResult.MethodNotFound;
            }
            catch (Exception ex)
            {
                if (PluginConfig.DebugEnabled)
                    Plugin.Log?.LogWarning(MicRecoveryText.Format(
                        "[MicRecovery] 调用游戏侧重置入口失败：{0}",
                        "[MicRecovery] Failed to call the game-side reset entry: {0}",
                        ex.Message));
                return GameSideResetResult.Failed;
            }
        }

        private static void LogGameSideResetCompatibilityWarningOnce(string reason)
        {
            if (_gameSideResetCompatibilityWarningLogged)
                return;

            _gameSideResetCompatibilityWarningLogged = true;
            Plugin.Log?.LogWarning(MicRecoveryText.Format(
                "[MicRecovery] 游戏侧重置入口不可用（{0}），已降级为仅执行本地 ResetMicrophoneCapture。",
                "[MicRecovery] Game-side reset entry is unavailable ({0}); degraded to local ResetMicrophoneCapture only.",
                reason));
        }

        private static System.Collections.IEnumerator SafeRunGameSideResetCoroutine(System.Collections.IEnumerator routine)
        {
            try
            {
                while (true)
                {
                    object current;
                    bool moveNext = false;
                    bool shouldStop = false;
                    try
                    {
                        moveNext = routine.MoveNext();
                        current = moveNext ? routine.Current : null;
                    }
                    catch (Exception ex)
                    {
                        shouldStop = true;
                        if (!_gameSideResetCoroutineWarningLogged)
                        {
                            _gameSideResetCoroutineWarningLogged = true;
                            Plugin.Log?.LogWarning(MicRecoveryText.Format(
                                "[MicRecovery] 游戏侧 ResetDissonanceCommsComponent 协程执行失败，已停止该协程并保留本地 ResetMicrophoneCapture 结果：{0}",
                                "[MicRecovery] Game-side ResetDissonanceCommsComponent coroutine failed; stopped it and kept the local ResetMicrophoneCapture result: {0}",
                                ex.Message));
                        }
                        else if (PluginConfig.DebugEnabled)
                        {
                            Plugin.Log?.LogWarning(MicRecoveryText.Format(
                                "[MicRecovery] 游戏侧 ResetDissonanceCommsComponent 协程再次失败：{0}",
                                "[MicRecovery] Game-side ResetDissonanceCommsComponent coroutine failed again: {0}",
                                ex.Message));
                        }
                        current = null;
                    }

                    if (shouldStop || !moveNext)
                        yield break;

                    yield return current;
                }
            }
            finally
            {
                _gameSideResetInFlight = false;
            }
        }
    }
}
