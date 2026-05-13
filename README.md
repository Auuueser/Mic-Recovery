# LC Mic Recovery

LC Mic Recovery is a BepInEx plugin for Lethal Company that attempts to recover a broken or stalled microphone capture pipeline without requiring the player to restart the game.

The plugin monitors the active Dissonance microphone state, detects common failure conditions, and can trigger a controlled recovery sequence either automatically or manually through a configurable keybind.

## Project Principles

The following bilingual notes summarize the design principles used for LC Mic Recovery: conservative recovery, local-first reset behavior, and guarded game-side integration.

<details open>
<summary>View principles document</summary>

<p align="center">
  <img src="docs/images/lcmicrecovery-principles-01.png" alt="LC Mic Recovery Principles - Page 1" width="820">
</p>

<p align="center">
  <img src="docs/images/lcmicrecovery-principles-02.png" alt="LC Mic Recovery Principles - Page 2" width="820">
</p>

<p align="center">
  <img src="docs/images/lcmicrecovery-principles-03.png" alt="LC Mic Recovery Principles - Page 3" width="820">
</p>

<p align="center">
  <img src="docs/images/lcmicrecovery-principles-04.png" alt="LC Mic Recovery Principles - Page 4" width="820">
</p>

</details>

## Features

- Automatic microphone recovery when the active microphone name is empty.
- Automatic recovery when the selected microphone is no longer present in the device list.
- Automatic recovery when Unity or Dissonance reports that microphone capture is not recording.
- Manual recovery keybind for cases where automatic detection is not sufficient.
- Recovery cooldown and post-recovery grace period to reduce repeated resets.
- Menu, lobby, teardown, and scene-transition guards to avoid unsafe recovery attempts.
- Local Dissonance microphone reset with guarded game-side reset support.
- Local recovery can continue when the game-side reset path is unsafe, while the game-side reset remains guarded.
- Safe wrapper around the game-side `ResetDissonanceCommsComponent` coroutine to prevent coroutine exceptions from escalating into repeated Unity errors.

## Requirements

- Lethal Company V81-era game assemblies.
- BepInEx.
- Harmony / 0Harmony.
- DissonanceVoip, UnityEngine, Unity.InputSystem, and Assembly-CSharp references from the game installation.

The project currently references local game and r2modman profile paths in `LCMicRecovery/LCMicRecovery.csproj`. If your installation paths differ, update the `HintPath` values before building.

## Build

From the repository root:

```powershell
dotnet build .\LCMicRecovery\LCMicRecovery.csproj -c Release
```

The compiled plugin is emitted to the configured output directory. Local build folders such as `bin`, `obj`, `tmpbin`, and `tmpobj` are intentionally ignored by git.

## Configuration

Configuration is managed through BepInEx config entries. Key options include:

- `EnableMod`
- `EnableAutoRecovery`
- `EnableManualRecoveryKey`
- `ManualRecoveryKey`
- `AutoCheckIntervalSeconds`
- `RecoveryCooldownSeconds`
- `PostRecoveryGraceSeconds`
- `SuspendAutoRecoveryWhenNoDevices`
- `SuspendAutoRecoveryDuringMenuOrTeardown`
- `AllowLocalRecoveryWhenGameSideUnsafe`
- `PreferredDeviceKeywords`

Default values are chosen for conservative behavior: automatic recovery is enabled, teardown/menu recovery is guarded, and manual recovery is available as a fallback.

## Recovery Behavior

The recovery sequence first tries to use the current microphone when it is still valid. If the current microphone is unavailable, it selects a preferred device by configured keyword, then falls back to the first valid device.

The local Dissonance reset is treated as the primary recovery mechanism. The game-side reset is guarded and executed only when the current game state appears safe. If the game-side reset is not safe, local `ResetMicrophoneCapture()` can still run when `AllowLocalRecoveryWhenGameSideUnsafe` is enabled.

## Notes

This project is intentionally small and conservative. Fixes should prefer local guards, clearer failure handling, and targeted compatibility checks over broad rewrites.
