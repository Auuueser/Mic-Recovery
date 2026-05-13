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
                SuspendRecoveryForTeardown("切换到主菜单/大厅场景，已暂停自动恢复。");
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

                bool isRecoveryBlockedByScene = MicRecoveryCore.IsRecoveryBlockedByScene();
                bool isGameSideResetSafe = MicRecoveryCore.IsGameSideResetSafe();
                bool showFiveStepLogs = PluginConfig.ShowFiveStepRecoveryLogs != null &&
                                        PluginConfig.ShowFiveStepRecoveryLogs.Value;

                if (PluginConfig.DebugEnabled || showFiveStepLogs)
                {
                    Plugin.Log?.LogInfo(
                        $"[MicRecovery] 手动恢复请求：menuScene={isRecoveryBlockedByScene}, gameSideResetSafe={isGameSideResetSafe}");
                }

                // 主菜单/大厅阶段不做手动恢复
                if (isRecoveryBlockedByScene)
                {
                    if (PluginConfig.DebugEnabled || showFiveStepLogs)
                        Plugin.Log?.LogInfo("[MicRecovery] 当前位于主菜单/大厅，已跳过手动恢复。");
                    return;
                }

                if (!isGameSideResetSafe && PluginConfig.DebugEnabled)
                {
                    Plugin.Log?.LogInfo("[MicRecovery] 游戏侧重置当前不安全，手动恢复仍将尝试本地 ResetMicrophoneCapture。");
                }

                var comms = GetComms();
                if (comms == null)
                {
                    if (PluginConfig.DebugEnabled || showFiveStepLogs)
                        Plugin.Log?.LogWarning("[MicRecovery] 手动恢复未找到 DissonanceComms，无法执行本地恢复。");
                    return;
                }

                if (PluginConfig.DebugEnabled || showFiveStepLogs)
                    Plugin.Log?.LogInfo("[MicRecovery] 手动恢复已找到 DissonanceComms。");

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
                        Plugin.Log?.LogWarning($"[MicRecovery] 手动恢复获取录音设备列表失败，将继续尝试强制恢复：{ex.Message}");
                }

                if (!deviceListReadFailed &&
                    _deviceBuffer.Count == 0 &&
                    PluginConfig.AllowManualRecoveryWhenNoDevices != null &&
                    !PluginConfig.AllowManualRecoveryWhenNoDevices.Value)
                {
                    if (PluginConfig.DebugEnabled)
                        Plugin.Log?.LogInfo("[MicRecovery] 当前没有录音设备，已跳过手动恢复。");
                    return;
                }

                MicRecoveryCore.TryRecover("手动按键触发恢复", true);
            }
            catch (Exception ex)
            {
                if (PluginConfig.DebugEnabled)
                    Plugin.Log?.LogWarning($"[MicRecovery] 手动按键检测失败：{ex.Message}");
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
            Plugin.Log?.LogInfo("[MicRecovery] Watcher 正在运行。");
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
                        SuspendRecoveryForTeardown("当前处于主菜单/大厅场景，已暂停自动恢复。");
                        return;
                    }

                    if (IsRecoveryTemporarilySuspended())
                        return;
                }

                var comms = GetComms();
                if (comms == null)
                {
                    if (PluginConfig.DebugEnabled)
                        Plugin.Log?.LogInfo("[MicRecovery] 当前未找到 DissonanceComms，跳过检测。");
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
                            Plugin.Log?.LogInfo("[MicRecovery] StartOfRound 尚未就绪，跳过自动恢复检测。");
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
                        Plugin.Log?.LogInfo("[MicRecovery] StartOfRound 尚未就绪，继续执行本地 Dissonance 检测，游戏侧重置将由恢复流程自行跳过。");
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
                        SuspendRecoveryForTeardown("检测到房间正在退出或语音上下文正在销毁，已暂停自动恢复。");
                        return;
                    }

                    if ((PluginConfig.DebugEnabled || PluginConfig.StateLogsEnabled) && !_teardownSuspendLogged)
                    {
                        Plugin.Log?.LogInfo("[MicRecovery] 游戏侧重置当前不安全，自动检测仍将允许本地 Dissonance 恢复。");
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
                        Plugin.Log?.LogWarning($"[MicRecovery] 自动检测获取录音设备列表失败（{_autoDeviceListFailureCount}/2）：{ex.Message}");

                    if (_autoDeviceListFailureCount >= 2)
                    {
                        _autoDeviceListFailureCount = 0;
                        MicRecoveryCore.TryRecover("自动检测连续获取录音设备列表失败");
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
                            Plugin.Log?.LogInfo("[MicRecovery] 未检测到任何录音设备，已暂停自动恢复与相关状态日志。");
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
                        Plugin.Log?.LogWarning($"[MicRecovery] 自动检测读取当前麦克风名称失败：{ex.Message}");
                    MicRecoveryCore.TryRecover("自动检测读取当前麦克风名称失败");
                    return;
                }

                if (PluginConfig.RecoverWhenMicNameEmpty.Value &&
                    string.IsNullOrWhiteSpace(currentMicName))
                {
                    MicRecoveryCore.TryRecover("当前 Dissonance 麦克风名称为空");
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
                            MicRecoveryCore.TryRecover("Dissonance 麦克风采集管线为空");
                            return;
                        }

                        if (!capture.IsRecording)
                        {
                            MicRecoveryCore.TryRecover("Dissonance 麦克风采集管线未在录音");
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        if (PluginConfig.DebugEnabled)
                            Plugin.Log?.LogWarning($"[MicRecovery] 读取 Dissonance 麦克风采集管线失败：{ex.Message}");
                    }
                }
                else if (PluginConfig.DebugEnabled)
                {
                    Plugin.Log?.LogInfo("[MicRecovery] 当前处于恢复宽限期，跳过 Dissonance 麦克风采集管线判定。");
                }

                bool exists = _deviceBuffer.Any(d => string.Equals(d, currentMicName, StringComparison.Ordinal));

                if (PluginConfig.StateLogsEnabled)
                {
                    Plugin.Log?.LogInfo($"[MicRecovery] 当前麦克风：{currentMicName} | 设备数：{_deviceBuffer.Count} | 设备仍存在：{exists}");
                }

                if (PluginConfig.RecoverWhenDeviceMissing.Value && !exists)
                {
                    MicRecoveryCore.TryRecover($"当前麦克风已不在设备列表中：{currentMicName}");
                    return;
                }

                if (inPostRecoveryGrace)
                {
                    if (PluginConfig.DebugEnabled)
                        Plugin.Log?.LogInfo("[MicRecovery] 当前处于恢复宽限期，跳过 Unity.IsRecording 判定。");
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
                        Plugin.Log?.LogWarning($"[MicRecovery] 调用 Microphone.IsRecording 失败：{ex.Message}");
                }

                if (PluginConfig.StateLogsEnabled)
                {
                    Plugin.Log?.LogInfo($"[MicRecovery] Unity.IsRecording({currentMicName}) = {recording}");
                }

                if (PluginConfig.RecoverWhenUnityNotRecording.Value && !recording)
                {
                    MicRecoveryCore.TryRecover($"Unity 报告麦克风未在录音：{currentMicName}");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"[MicRecovery] AutoCheckMic 异常：{ex}");
                _cachedComms = null;
                _nextCommsSearchTime = 0f;
            }
        }
    }
}
