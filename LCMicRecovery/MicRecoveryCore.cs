using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
            SkippedUnsafe,
            MethodNotFound,
            Failed
        }

        private static readonly BindingFlags InstanceFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly BindingFlags StaticFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        private static float _lastRecoveryTime = -999f;
        private static float _postRecoveryGraceUntil = -999f;
        private static bool _gameSideResetCompatibilityWarningLogged;
        private static bool _gameSideResetCoroutineWarningLogged;

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
                    Plugin.Log?.LogWarning($"[MicRecovery] 恢复冷却中，跳过本次恢复。reason={reason}");
                return false;
            }

            LogRecoveryStep(1, "检测到疑似麦克风失效，开始执行恢复");
            LogRecoveryStep(2, $"触发原因：{reason}");

            try
            {
                var comms = UnityEngine.Object.FindObjectOfType<DissonanceComms>();
                if (comms == null)
                {
                    Plugin.Log?.LogError("========== [麦克风修复 3/5] 未找到 DissonanceComms，无法执行恢复 ==========");
                    Plugin.Log?.LogError("========== [麦克风修复 4/5] 本次恢复失败 ==========");
                    Plugin.Log?.LogError("========== [麦克风修复 5/5] 请检查当前场景是否已进入可语音状态 ==========");
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
                        Plugin.Log?.LogWarning($"[MicRecovery] 读取当前麦克风名称失败：{ex.Message}");
                }

                var deviceList = new List<string>();
                try
                {
                    comms.GetMicrophoneDevices(deviceList);
                }
                catch (Exception ex)
                {
                    if (PluginConfig.DebugEnabled)
                        Plugin.Log?.LogWarning($"[MicRecovery] 获取麦克风列表失败：{ex.Message}");
                }

                string[] devices = deviceList.ToArray();
                LogRecoveryStep(3, $"当前麦克风：{(string.IsNullOrEmpty(currentMicName) ? "<空>" : currentMicName)} | 设备数：{devices.Length}");

                string preferred = PickPreferredDevice(devices, currentMicName);
                if (!string.IsNullOrEmpty(preferred))
                {
                    if (!string.Equals(currentMicName, preferred, StringComparison.Ordinal))
                    {
                        comms.MicrophoneName = preferred;
                        if (PluginConfig.DebugEnabled)
                            Plugin.Log?.LogInfo($"[MicRecovery] 已切换麦克风到首选设备：{preferred}");
                    }
                    else
                    {
                        if (PluginConfig.DebugEnabled)
                            Plugin.Log?.LogInfo($"[MicRecovery] 当前已是首选设备：{preferred}");
                    }
                }
                else
                {
                    Plugin.Log?.LogWarning("[MicRecovery] 没找到可用的首选设备，将沿用当前设备继续恢复。");
                }

                comms.ResetMicrophoneCapture();
                if (PluginConfig.DebugEnabled || PluginConfig.StateLogsEnabled)
                    Plugin.Log?.LogInfo("[MicRecovery] 本地 ResetMicrophoneCapture 已成功执行。");

                _lastRecoveryTime = Time.unscaledTime;

                // 只有在游戏侧上下文明确安全时，才调用游戏自己的重置入口
                GameSideResetResult gameSideResetResult = TryInvokeGameSideReset();
                if (PluginConfig.DebugEnabled || PluginConfig.StateLogsEnabled)
                    Plugin.Log?.LogInfo($"[MicRecovery] Game side reset result: {gameSideResetResult}");

                float grace = PluginConfig.PostRecoveryGraceSeconds != null
                    ? Mathf.Max(0f, PluginConfig.PostRecoveryGraceSeconds.Value)
                    : 4f;
                _postRecoveryGraceUntil = Time.unscaledTime + grace;

                switch (gameSideResetResult)
                {
                    case GameSideResetResult.Invoked:
                        LogRecoveryStep(4, "已执行 ResetMicrophoneCapture，已启动游戏侧重置");
                        break;
                    case GameSideResetResult.SkippedUnsafe:
                        LogRecoveryStep(4, "已执行 ResetMicrophoneCapture，已跳过游戏侧重置（当前阶段不安全）");
                        break;
                    case GameSideResetResult.MethodNotFound:
                        LogRecoveryStep(4, "已执行 ResetMicrophoneCapture，未执行游戏侧重置（未找到重置方法）");
                        break;
                    default:
                        LogRecoveryStep(4, "已执行 ResetMicrophoneCapture，未执行游戏侧重置（调用失败）");
                        break;
                }
                LogRecoveryStep(5, $"麦克风恢复流程已触发，进入 {grace:0.##} 秒恢复宽限期，请立刻测试语音是否恢复");

                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError("========== [麦克风修复 3/5] 恢复过程中出现异常 ==========");
                Plugin.Log?.LogError($"========== [麦克风修复 4/5] 异常：{ex} ==========");
                Plugin.Log?.LogError("========== [麦克风修复 5/5] 本次恢复失败，请把日志贴回来 ==========");
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

        private static bool TryGetStartOfRoundInstance(out Type startOfRoundType, out object startOfRoundInstance)
        {
            startOfRoundType = Type.GetType("StartOfRound, Assembly-CSharp");
            startOfRoundInstance = null;

            if (startOfRoundType == null)
                return false;

            var instanceProp = startOfRoundType.GetProperty("Instance", StaticFlags);
            if (instanceProp != null)
                startOfRoundInstance = instanceProp.GetValue(null);

            if (startOfRoundInstance == null)
            {
                var instanceField = startOfRoundType.GetField("Instance", StaticFlags);
                if (instanceField != null)
                    startOfRoundInstance = instanceField.GetValue(null);
            }

            if (startOfRoundInstance is UnityEngine.Object unityObj && unityObj == null)
                startOfRoundInstance = null;

            return startOfRoundInstance != null;
        }

        internal static bool IsStartOfRoundReady()
        {
            return TryGetStartOfRoundInstance(out _, out _);
        }

        internal static bool IsGameSideResetSafe()
        {
            try
            {
                if (IsMenuSceneActive())
                    return false;

                if (!TryGetStartOfRoundInstance(out Type startOfRoundType, out object startOfRoundInstance))
                    return false;

                var localPlayerField = startOfRoundType.GetField("localPlayerController", InstanceFlags);
                if (localPlayerField != null)
                {
                    object localPlayer = localPlayerField.GetValue(startOfRoundInstance);
                    if (localPlayer == null)
                        return false;

                    if (localPlayer is UnityEngine.Object playerObj && playerObj == null)
                        return false;
                }

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
                Plugin.Log?.LogWarning($"========== [麦克风修复 {step}/5] {message} ==========");
            else if (PluginConfig.DebugEnabled)
                Plugin.Log?.LogInfo($"[MicRecovery] Step {step}/5: {message}");
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
                        Plugin.Log?.LogInfo("[MicRecovery] 当前不适合调用游戏侧重置入口，已跳过 ResetDissonanceCommsComponent。");
                    return GameSideResetResult.SkippedUnsafe;
                }

                if (!TryGetStartOfRoundInstance(out Type startOfRoundType, out object startOfRoundInstance))
                {
                    LogGameSideResetCompatibilityWarningOnce("无法获取 StartOfRound 实例");
                    return GameSideResetResult.SkippedUnsafe;
                }

                var resetMethod = startOfRoundType.GetMethod(
                    "ResetDissonanceCommsComponent",
                    InstanceFlags);

                if (resetMethod == null)
                {
                    LogGameSideResetCompatibilityWarningOnce("未找到 ResetDissonanceCommsComponent 方法");
                    if (PluginConfig.DebugEnabled)
                        Plugin.Log?.LogWarning("[MicRecovery] 未找到 ResetDissonanceCommsComponent 方法。");
                    return GameSideResetResult.MethodNotFound;
                }

                var result = resetMethod.Invoke(startOfRoundInstance, null);

                if (result is System.Collections.IEnumerator routine && Plugin.Instance != null)
                {
                    Plugin.Instance.StartCoroutine(SafeRunGameSideResetCoroutine(routine));
                }

                return GameSideResetResult.Invoked;
            }
            catch (Exception ex)
            {
                if (PluginConfig.DebugEnabled)
                    Plugin.Log?.LogWarning($"[MicRecovery] 调用游戏侧重置入口失败：{ex.Message}");
                return GameSideResetResult.Failed;
            }
        }

        private static void LogGameSideResetCompatibilityWarningOnce(string reason)
        {
            if (_gameSideResetCompatibilityWarningLogged)
                return;

            _gameSideResetCompatibilityWarningLogged = true;
            Plugin.Log?.LogWarning($"[MicRecovery] 游戏侧重置入口不可用（{reason}），已降级为仅执行本地 ResetMicrophoneCapture。");
        }

        private static System.Collections.IEnumerator SafeRunGameSideResetCoroutine(System.Collections.IEnumerator routine)
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
                        Plugin.Log?.LogWarning($"[MicRecovery] 游戏侧 ResetDissonanceCommsComponent 协程执行失败，已停止该协程并保留本地 ResetMicrophoneCapture 结果：{ex.Message}");
                    }
                    else if (PluginConfig.DebugEnabled)
                    {
                        Plugin.Log?.LogWarning($"[MicRecovery] 游戏侧 ResetDissonanceCommsComponent 协程再次失败：{ex.Message}");
                    }
                    current = null;
                }

                if (shouldStop || !moveNext)
                    yield break;

                yield return current;
            }
        }
    }
}
