# Changelog

All notable changes to LC Mic Recovery are documented in this file.

## 0.3.7

### Added

- Added `LanguageMode` with `Auto`, `English`, and `Chinese` modes for user-facing logs and recovery notifications.
- Added LC-Chinese-Project / `V81TestChn` detection so `Auto` mode follows Chinese modpacks while keeping vanilla game installs in English.
- Added HUD font-safety fallback behavior: `Chinese` mode keeps BepInEx logs in Chinese while in-game HUD notifications fall back to English when Chinese HUD font support is not detected.
- Added recovery completion detail logging when five-step, debug, or state logging is enabled, so the full recovery note remains available outside the compact HUD notification.

### Changed

- Updated manual recovery, automatic recovery, five-step recovery, game-side reset, and startup logs to use the selected user-facing language.
- Recovery completion notifications now use a compact in-game HUD message, while detailed completion context is recorded in the BepInEx log.

## 0.3.6

### Added

- Added `AllowLocalRecoveryWhenGameSideUnsafe`, enabled by default, to allow local Dissonance microphone recovery even when the game-side reset path is not safe to invoke.

### Changed

- Manual recovery no longer skips the entire recovery flow only because `StartOfRound.ResetDissonanceCommsComponent` is currently unsafe.
- Automatic recovery can continue local Dissonance detection and local `ResetMicrophoneCapture()` when `StartOfRound` or `localPlayerController` is not ready.
- Recovery logs now make manual trigger state, local reset execution, and game-side reset results easier to distinguish when debug or five-step logging is enabled.
- Project references now point at the current local V81 test profile while keeping game assembly references under the local Lethal Company installation.

### Fixed

- Fixed cases where both manual and automatic recovery were skipped too early because game-side reset safety was treated as a blocker for local microphone recovery.

## 0.3.5

### Added

- Added automatic detection for Dissonance microphone capture pipeline failures.
- Added manual recovery hardening so manual recovery can bypass the normal recovery cooldown.
- Added safe execution wrapper for the game-side `ResetDissonanceCommsComponent` coroutine.
- Added watcher duplicate-creation guard.
- Added one-time compatibility warning when the game-side reset entry point is unavailable.
- Added local protections for destroyed cached Unity objects.

### Changed

- Recovery cooldown is now recorded only after local `ResetMicrophoneCapture()` succeeds.
- Game-side reset logging now distinguishes between skipped, unavailable, failed, and started reset paths.
- Automatic recovery now avoids immediate recovery after a single transient device-list read failure.
- Dissonance capture checks now respect the post-recovery grace period.
- Manual recovery continues when device-list enumeration fails, while preserving the configured behavior for confirmed no-device states.
- Assembly and file versions now match the published plugin version.

### Fixed

- Fixed failed recovery attempts consuming the normal recovery cooldown.
- Fixed manual recovery being able to run during unsafe teardown or scene-transition states.
- Fixed misleading logs that reported game-side reset as completed when it had only been skipped or started.
- Fixed Unity coroutine exceptions from the game-side reset path surfacing as repeated unhandled errors.

## 0.1.0

### Added

- Initial BepInEx plugin structure.
- Basic Dissonance microphone recovery flow.
- Automatic and manual recovery entry points.
