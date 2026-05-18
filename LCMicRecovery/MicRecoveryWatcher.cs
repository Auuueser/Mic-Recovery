using System;
using System.Collections.Generic;
using System.Linq;
using Dissonance;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace LCMicRecovery
{
    public class MicRecoveryWatcher : MonoBehaviour
    {
        private float _checkTimer;
        private float _heartbeatTimer;
        private float _nextCommsSearchTime;
        private float _lastManualRecoveryTime = -999f;
        private float _suspendRecoveryUntil = -999f;

        private bool _preRoundSkipLogged;
        private bool _noDeviceSuspendLogged;
        private bool _teardownSuspendLogged;
        private int _autoDeviceListFailureCount;

        private DissonanceComms _cachedComms;
        private readonly List<string> _deviceBuffer = new List<string>(8);

        private void OnEnable()
        {
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
        }

        private void OnDisable()
        {
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        }

        private void OnActiveSceneChanged(Scene oldScene, Scene newScene)
        {
            _cachedComms = null;
            _nextCommsSearchTime = 0f;

            _preRoundSkipLogged = false;
            _noDeviceSuspendLogged = false;
            _teardownSuspendLogged = false;
            _autoDeviceListFailureCount = 0;

            if (PluginConfig.SuspendAutoRecoveryDuringMenuOrTeardown != null &&
                PluginConfig.SuspendAutoRecoveryDuringMenuOrTeardown.Value &&
                IsMenuScene(newScene))
            {
                SuspendRecoveryForTeardown(MicRecoveryText.T(
                    "切换到主菜单/大厅场景，已暂停自动恢复。",
                    "Switched to the main menu/lobby scene; automatic recovery is suspended."));
            }
        }

        private static bool IsMenuScene(Scene scene)
        {
            if (!scene.IsValid())
                return true;

            string name = scene.name ?? string.Empty;
            return name.IndexOf("menu", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsRecoveryTemporarilySuspended()
        {
            return Time.unscaledTime < _suspendRecoveryUntil;
        }

        private void SuspendRecoveryForTeardown(string reason)
        {
            float seconds = PluginConfig.LobbyExitSuspendSeconds != null
                ? Mathf.Max(0f, PluginConfig.LobbyExitSuspendSeconds.Value)
                : 8f;

            float until = Time.unscaledTime + seconds;
            if (until > _suspendRecoveryUntil)
                _suspendRecoveryUntil = until;

            if (PluginConfig.EnableTeardownSuspendLog != null &&
                PluginConfig.EnableTeardownSuspendLog.Value)
            {
                bool onlyOnce = PluginConfig.LogTeardownSuspendOnlyOnce != null &&
                                PluginConfig.LogTeardownSuspendOnlyOnce.Value;

                if (!onlyOnce || !_teardownSuspendLogged)
                {
                    Plugin.Log?.LogInfo($"[MicRecovery] {reason}");
                    _teardownSuspendLogged = true;
                }
            }
        }

        private void Update()
        {
            if (PluginConfig.EnableMod != null && !PluginConfig.EnableMod.Value)
                return;

            HandleManualRecoveryKey();
            HandleHeartbeat();

            if (PluginConfig.EnableAutoRecovery != null && !PluginConfig.EnableAutoRecovery.Value)
                return;

            float interval = PluginConfig.AutoCheckIntervalSeconds != null
                ? Mathf.Max(0.5f, PluginConfig.AutoCheckIntervalSeconds.Value)
                : 3f;

            _checkTimer += Time.unscaledDeltaTime;
            if (_checkTimer < interval)
                return;

            _checkTimer = 0f;
            AutoCheckMic();
        }

        private void HandleManualRecoveryKey()
        {
            if (PluginConfig.EnableManualRecoveryKey == null || !PluginConfig.EnableManualRecoveryKey.Value)
                return;

            try
            {
                var keyboard = Keyboard.current;
                if (keyboard == null)
                    return;

                var key = PluginConfig.ManualRecoveryKey.Value;
                if (!keyboard[key].wasPressedThisFrame)
                    return;

                if (Time.unscaledTime - _lastManualRecoveryTime < 2f)
                    return;

                bool isRecoveryBlockedByScene = MicRecoveryCore.IsRecoveryBlockedByScene();
                bool isGameSideResetSafe = MicRecoveryCore.IsGameSideResetSafe();
                bool showFiveStepLogs = PluginConfig.ShowFiveStepRecoveryLogs != null &&
                                        PluginConfig.ShowFiveStepRecoveryLogs.Value;

                if (PluginConfig.DebugEnabled || showFiveStepLogs)
                {
                    Plugin.Log?.LogInfo(MicRecoveryText.Format(
                        "[MicRecovery] 手动恢复请求：menuScene={0}, gameSideResetSafe={1}",
                        "[MicRecovery] Manual recovery requested: menuScene={0}, gameSideResetSafe={1}",
                        isRecoveryBlockedByScene,
                        isGameSideResetSafe));
                }

                // 主菜单/大厅阶段不做手动恢复
                if (isRecoveryBlockedByScene)
                {
                    if (PluginConfig.DebugEnabled || showFiveStepLogs)
                        Plugin.Log?.LogInfo(MicRecoveryText.T(
                            "[MicRecovery] 当前位于主菜单/大厅，已跳过手动恢复。",
                            "[MicRecovery] Current scene is the main menu/lobby; manual recovery was skipped."));
                    return;
                }

                if (PluginConfig.SuspendAutoRecoveryDuringMenuOrTeardown != null &&
                    PluginConfig.SuspendAutoRecoveryDuringMenuOrTeardown.Value &&
                    IsRecoveryTemporarilySuspended())
                {
                    if (PluginConfig.DebugEnabled || showFiveStepLogs)
                        Plugin.Log?.LogInfo(MicRecoveryText.T(
                            "[MicRecovery] 当前处于退出/切场景暂停窗口，已跳过手动恢复。",
                            "[MicRecovery] Currently in the exit/scene-switch suspend window; manual recovery was skipped."));
                    return;
                }

                if (!isGameSideResetSafe && PluginConfig.DebugEnabled)
                {
                    Plugin.Log?.LogInfo(MicRecoveryText.T(
                        "[MicRecovery] 游戏侧重置当前不安全，手动恢复仍将尝试本地 ResetMicrophoneCapture。",
                        "[MicRecovery] Game-side reset is currently unsafe; manual recovery will still try local ResetMicrophoneCapture."));
                }

                var comms = GetComms();
                if (comms == null)
                {
                    if (PluginConfig.DebugEnabled || showFiveStepLogs)
                        Plugin.Log?.LogWarning(MicRecoveryText.T(
                            "[MicRecovery] 手动恢复未找到 DissonanceComms，无法执行本地恢复。",
                            "[MicRecovery] Manual recovery could not find DissonanceComms; local recovery cannot run."));
                    return;
                }

                if (PluginConfig.DebugEnabled || showFiveStepLogs)
                    Plugin.Log?.LogInfo(MicRecoveryText.T(
                        "[MicRecovery] 手动恢复已找到 DissonanceComms。",
                        "[MicRecovery] Manual recovery found DissonanceComms."));

                _deviceBuffer.Clear();
                bool deviceListReadFailed = false;
                try
                {
                    comms.GetMicrophoneDevices(_deviceBuffer);
                }
                catch (Exception ex)
                {
                    deviceListReadFailed = true;
                    if (PluginConfig.DebugEnabled)
                        Plugin.Log?.LogWarning(MicRecoveryText.Format(
                            "[MicRecovery] 手动恢复获取录音设备列表失败，将继续尝试强制恢复：{0}",
                            "[MicRecovery] Manual recovery failed to get the recording device list; continuing forced recovery: {0}",
                            ex.Message));
                }

                if (!deviceListReadFailed &&
                    _deviceBuffer.Count == 0 &&
                    PluginConfig.AllowManualRecoveryWhenNoDevices != null &&
                    !PluginConfig.AllowManualRecoveryWhenNoDevices.Value)
                {
                    if (PluginConfig.DebugEnabled)
                        Plugin.Log?.LogInfo(MicRecoveryText.T(
                            "[MicRecovery] 当前没有录音设备，已跳过手动恢复。",
                            "[MicRecovery] No recording devices are currently available; manual recovery was skipped."));
                    return;
                }

                _lastManualRecoveryTime = Time.unscaledTime;
                MicRecoveryCore.TryRecover(MicRecoveryText.T("手动按键触发恢复", "Manual recovery key pressed"), true);
            }
            catch (Exception ex)
            {
                if (PluginConfig.DebugEnabled)
                    Plugin.Log?.LogWarning(MicRecoveryText.Format(
                        "[MicRecovery] 手动按键检测失败：{0}",
                        "[MicRecovery] Manual recovery key check failed: {0}",
                        ex.Message));
            }
        }

        private void HandleHeartbeat()
        {
            if (!PluginConfig.HeartbeatEnabled)
                return;

            _heartbeatTimer += Time.unscaledDeltaTime;
            if (_heartbeatTimer < 15f)
                return;

            _heartbeatTimer = 0f;
            Plugin.Log?.LogInfo(MicRecoveryText.T(
                "[MicRecovery] Watcher 正在运行。",
                "[MicRecovery] Watcher is running."));
        }

        private DissonanceComms GetComms()
        {
            if (_cachedComms is UnityEngine.Object cachedObj && cachedObj == null)
                _cachedComms = null;

            if (_cachedComms != null)
                return _cachedComms;

            if (Time.unscaledTime < _nextCommsSearchTime)
                return null;

            _nextCommsSearchTime = Time.unscaledTime + 1f;
            _cachedComms = UnityEngine.Object.FindObjectOfType<DissonanceComms>();
            return _cachedComms;
        }

        private void AutoCheckMic()
        {
            try
            {
                if (PluginConfig.SuspendAutoRecoveryDuringMenuOrTeardown != null &&
                    PluginConfig.SuspendAutoRecoveryDuringMenuOrTeardown.Value)
                {
                    if (MicRecoveryCore.IsMenuSceneActive())
                    {
                        SuspendRecoveryForTeardown(MicRecoveryText.T(
                            "当前处于主菜单/大厅场景，已暂停自动恢复。",
                            "Current scene is the main menu/lobby; automatic recovery is suspended."));
                        return;
                    }

                    if (IsRecoveryTemporarilySuspended())
                        return;
                }

                var comms = GetComms();
                if (comms == null)
                {
                    if (PluginConfig.DebugEnabled)
                        Plugin.Log?.LogInfo(MicRecoveryText.T(
                            "[MicRecovery] 当前未找到 DissonanceComms，跳过检测。",
                            "[MicRecovery] DissonanceComms is not currently available; skipping detection."));
                    return;
                }

                bool allowLocalWhenGameSideUnsafe = PluginConfig.AllowLocalRecoveryWhenGameSideUnsafe == null ||
                                                    PluginConfig.AllowLocalRecoveryWhenGameSideUnsafe.Value;
                bool isStartOfRoundReady = MicRecoveryCore.IsStartOfRoundReady();
                bool isGameSideResetSafe = MicRecoveryCore.IsGameSideResetSafe();

                if (!PluginConfig.EnablePreRoundRecovery.Value && !isStartOfRoundReady && !allowLocalWhenGameSideUnsafe)
                {
                    if (PluginConfig.EnablePreRoundSkipLog.Value)
                    {
                        bool onlyOnce = PluginConfig.LogPreRoundSkipOnlyOnce.Value;
                        if (!onlyOnce || !_preRoundSkipLogged)
                        {
                            Plugin.Log?.LogInfo(MicRecoveryText.T(
                                "[MicRecovery] StartOfRound 尚未就绪，跳过自动恢复检测。",
                                "[MicRecovery] StartOfRound is not ready; skipping automatic recovery detection."));
                            _preRoundSkipLogged = true;
                        }
                    }

                    return;
                }

                if (!isStartOfRoundReady && PluginConfig.EnablePreRoundSkipLog.Value && allowLocalWhenGameSideUnsafe)
                {
                    bool onlyOnce = PluginConfig.LogPreRoundSkipOnlyOnce.Value;
                    if (!onlyOnce || !_preRoundSkipLogged)
                    {
                        Plugin.Log?.LogInfo(MicRecoveryText.T(
                            "[MicRecovery] StartOfRound 尚未就绪，继续执行本地 Dissonance 检测，游戏侧重置将由恢复流程自行跳过。",
                            "[MicRecovery] StartOfRound is not ready; continuing local Dissonance detection. Game-side reset will be skipped by recovery flow."));
                        _preRoundSkipLogged = true;
                    }
                }

                if (isStartOfRoundReady)
                    _preRoundSkipLogged = false;

                // 退出房间/对象销毁阶段：默认允许本地检测，游戏侧重置由 TryRecover 内部继续保护。
                if (PluginConfig.SuspendAutoRecoveryDuringMenuOrTeardown.Value &&
                    !isGameSideResetSafe)
                {
                    if (!allowLocalWhenGameSideUnsafe)
                    {
                        SuspendRecoveryForTeardown(MicRecoveryText.T(
                            "检测到房间正在退出或语音上下文正在销毁，已暂停自动恢复。",
                            "Room exit or voice context teardown detected; automatic recovery is suspended."));
                        return;
                    }

                    if ((PluginConfig.DebugEnabled || PluginConfig.StateLogsEnabled) && !_teardownSuspendLogged)
                    {
                        Plugin.Log?.LogInfo(MicRecoveryText.T(
                            "[MicRecovery] 游戏侧重置当前不安全，自动检测仍将允许本地 Dissonance 恢复。",
                            "[MicRecovery] Game-side reset is currently unsafe; automatic detection will still allow local Dissonance recovery."));
                        _teardownSuspendLogged = true;
                    }
                }
                else
                {
                    _teardownSuspendLogged = false;
                }

                _deviceBuffer.Clear();
                try
                {
                    comms.GetMicrophoneDevices(_deviceBuffer);
                }
                catch (Exception ex)
                {
                    _autoDeviceListFailureCount++;
                    if (PluginConfig.DebugEnabled)
                        Plugin.Log?.LogWarning(MicRecoveryText.Format(
                            "[MicRecovery] 自动检测获取录音设备列表失败（{0}/2）：{1}",
                            "[MicRecovery] Automatic detection failed to get the recording device list ({0}/2): {1}",
                            _autoDeviceListFailureCount,
                            ex.Message));

                    if (_autoDeviceListFailureCount >= 2)
                    {
                        _autoDeviceListFailureCount = 0;
                        MicRecoveryCore.TryRecover(MicRecoveryText.T(
                            "自动检测连续获取录音设备列表失败",
                            "Automatic detection failed to get the recording device list repeatedly"));
                    }

                    return;
                }

                _autoDeviceListFailureCount = 0;

                if (PluginConfig.SuspendAutoRecoveryWhenNoDevices.Value && _deviceBuffer.Count == 0)
                {
                    if (PluginConfig.EnableNoDeviceSuspendLog.Value)
                    {
                        bool onlyOnce = PluginConfig.LogNoDeviceSuspendOnlyOnce.Value;
                        if (!onlyOnce || !_noDeviceSuspendLogged)
                        {
                            Plugin.Log?.LogInfo(MicRecoveryText.T(
                                "[MicRecovery] 未检测到任何录音设备，已暂停自动恢复与相关状态日志。",
                                "[MicRecovery] No recording devices were detected; automatic recovery and related state logs are suspended."));
                            _noDeviceSuspendLogged = true;
                        }
                    }

                    return;
                }

                _noDeviceSuspendLogged = false;

                string currentMicName;
                try
                {
                    currentMicName = comms.MicrophoneName;
                }
                catch (Exception ex)
                {
                    if (PluginConfig.DebugEnabled)
                        Plugin.Log?.LogWarning(MicRecoveryText.Format(
                            "[MicRecovery] 自动检测读取当前麦克风名称失败：{0}",
                            "[MicRecovery] Automatic detection failed to read the current microphone name: {0}",
                            ex.Message));
                    MicRecoveryCore.TryRecover(MicRecoveryText.T(
                        "自动检测读取当前麦克风名称失败",
                        "Automatic detection failed to read the current microphone name"));
                    return;
                }

                if (PluginConfig.RecoverWhenMicNameEmpty.Value &&
                    string.IsNullOrWhiteSpace(currentMicName))
                {
                    MicRecoveryCore.TryRecover(MicRecoveryText.T(
                        "当前 Dissonance 麦克风名称为空",
                        "Current Dissonance microphone name is empty"));
                    return;
                }

                bool inPostRecoveryGrace = MicRecoveryCore.IsInPostRecoveryGracePeriod;
                if (!inPostRecoveryGrace)
                {
                    try
                    {
                        var capture = comms.MicrophoneCapture;
                        if (capture == null)
                        {
                            MicRecoveryCore.TryRecover(MicRecoveryText.T(
                                "Dissonance 麦克风采集管线为空",
                                "Dissonance microphone capture pipeline is null"));
                            return;
                        }

                        if (capture is UnityEngine.Object captureObj && captureObj == null)
                        {
                            _cachedComms = null;
                            _nextCommsSearchTime = 0f;
                            MicRecoveryCore.TryRecover(MicRecoveryText.T(
                                "Dissonance 麦克风采集管线已被销毁",
                                "Dissonance microphone capture pipeline has been destroyed"));
                            return;
                        }

                        if (!capture.IsRecording)
                        {
                            MicRecoveryCore.TryRecover(MicRecoveryText.T(
                                "Dissonance 麦克风采集管线未在录音",
                                "Dissonance microphone capture pipeline is not recording"));
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        if (PluginConfig.DebugEnabled)
                            Plugin.Log?.LogWarning(MicRecoveryText.Format(
                                "[MicRecovery] 读取 Dissonance 麦克风采集管线失败：{0}",
                                "[MicRecovery] Failed to read the Dissonance microphone capture pipeline: {0}",
                                ex.Message));
                    }
                }
                else if (PluginConfig.DebugEnabled)
                {
                    Plugin.Log?.LogInfo(MicRecoveryText.T(
                        "[MicRecovery] 当前处于恢复宽限期，跳过 Dissonance 麦克风采集管线判定。",
                        "[MicRecovery] Currently in the recovery grace period; skipping Dissonance microphone capture pipeline checks."));
                }

                bool exists = _deviceBuffer.Any(d => string.Equals(d, currentMicName, StringComparison.Ordinal));

                if (PluginConfig.StateLogsEnabled)
                {
                    Plugin.Log?.LogInfo(MicRecoveryText.Format(
                        "[MicRecovery] 当前麦克风：{0} | 设备数：{1} | 设备仍存在：{2}",
                        "[MicRecovery] Current microphone: {0} | Device count: {1} | Device still exists: {2}",
                        currentMicName,
                        _deviceBuffer.Count,
                        exists));
                }

                if (PluginConfig.RecoverWhenDeviceMissing.Value && !exists)
                {
                    MicRecoveryCore.TryRecover(MicRecoveryText.Format(
                        "当前麦克风已不在设备列表中：{0}",
                        "Current microphone is no longer in the device list: {0}",
                        currentMicName));
                    return;
                }

                if (inPostRecoveryGrace)
                {
                    if (PluginConfig.DebugEnabled)
                        Plugin.Log?.LogInfo(MicRecoveryText.T(
                            "[MicRecovery] 当前处于恢复宽限期，跳过 Unity.IsRecording 判定。",
                            "[MicRecovery] Currently in the recovery grace period; skipping Unity.IsRecording check."));
                    return;
                }

                bool recording = false;
                try
                {
                    if (!string.IsNullOrWhiteSpace(currentMicName))
                        recording = Microphone.IsRecording(currentMicName);
                }
                catch (Exception ex)
                {
                    if (PluginConfig.DebugEnabled)
                        Plugin.Log?.LogWarning(MicRecoveryText.Format(
                            "[MicRecovery] 调用 Microphone.IsRecording 失败：{0}",
                            "[MicRecovery] Microphone.IsRecording call failed: {0}",
                            ex.Message));
                }

                if (PluginConfig.StateLogsEnabled)
                {
                    Plugin.Log?.LogInfo($"[MicRecovery] Unity.IsRecording({currentMicName}) = {recording}");
                }

                if (PluginConfig.RecoverWhenUnityNotRecording.Value && !recording)
                {
                    MicRecoveryCore.TryRecover(MicRecoveryText.Format(
                        "Unity 报告麦克风未在录音：{0}",
                        "Unity reports that the microphone is not recording: {0}",
                        currentMicName));
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning(MicRecoveryText.Format(
                    "[MicRecovery] AutoCheckMic 异常：{0}",
                    "[MicRecovery] AutoCheckMic exception: {0}",
                    ex));
                _cachedComms = null;
                _nextCommsSearchTime = 0f;
            }
        }
    }
}
