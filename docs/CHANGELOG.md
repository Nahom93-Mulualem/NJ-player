# Changelog

All notable changes to NJ Player will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **Video Host & Playback Integration (`NJPlayer.App`)**:
  - `VideoHost`: `HwndHost` subclass creating a Win32 child window with `WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN`, passing its handle (`wid`) to mpv for zero-copy native video rendering.
  - Video playback integration with `hwdec=auto-safe` (D3D11VA / NVDEC / VAAPI / Vulkan hardware decoding) and real-time active decoder query via `hwdec-current`.
  - Drag-and-drop support on `MainWindow` for instant video file loading.
  - Standard `Ctrl+O` hotkey and file picker dialog for opening media files.
  - `Space` hotkey for instant play/pause cycling.
  - Minimal telemetry and debug bar displaying active hardware decoder status (`hwdec`), file title, playback badge (`PLAYING` / `PAUSED`), and elapsed/total duration (`00:00 / 00:00`).
  - Empty state overlay with glowing logo emblem and clear drop instructions, plus graceful fallback banner with download instructions when `libmpv-2.dll` is not yet installed.
- **Core mpv Wrapper & Playback Engine (`NJPlayer.Core`)**:
  - `MpvNative`: P/Invoke signatures and struct definitions for `libmpv-2.dll` with 64-bit calling conventions.
  - `MpvContext`: SafeHandle wrapper managing `mpv_handle` life cycle, thread synchronization lock, and safe UTF-8 string array marshaling for `mpv_command` (zero string concatenation for paths).
  - `MpvOptionBuilder`: Pre-initialization builder for hardware decoding (`auto-safe`), keep-open, idle, terminal suppression, and window embedding (`wid`).
  - `MpvPlayer`: High-level engine exposing `Load`, `Play`, `Pause`, `TogglePause`, `Stop`, `Seek` (fast/exact), `SetVolume`, and `SetSpeed`.
  - Dedicated event-loop worker thread waking on `mpv_set_wakeup_callback` with an `AutoResetEvent` (zero CPU waste, zero tight-loop polling).
  - Synchronized observation of `time-pos`, `duration`, `pause`, `volume`, `speed`, and `track-list`.
  - Standard .NET events: `TimePositionChanged`, `DurationChanged`, `PauseChanged`, `VolumeChanged`, `SpeedChanged`, `TrackListChanged`, `EndFile`, and `Error`.
  - Unit test suite (`tests/NJPlayer.Tests/MpvLogicTests.cs`) covering option building, event mapping, time conversions, and command arguments formatting (20 tests passed).
- Master branding and multi-resolution Windows icon suite in `src/NJPlayer.App/Assets`:
  - `app.ico` containing 16x16, 32x32, 48x48, and 256x256 32-bit RGBA frames for Windows Shell compatibility.
  - Individual resolution PNG exports and master `logo.png`.
  - Configured `ApplicationIcon` in `NJPlayer.App.csproj` and window icon in `MainWindow.xaml`.
- Initial project scaffolding for NJ Player solution (`NJPlayer.sln`):
  - `src/NJPlayer.Core`: C# class library for libmpv wrapper, playback engine, settings, and hotkey management.
  - `src/NJPlayer.App`: WPF application for dark frameless video player and transparent overlay interface.
  - `src/NJPlayer.Features`: C# class library for secondary features (subtitles, trimmer, downloader, radio, equalizer).
  - `tests/NJPlayer.Tests`: xUnit test project for core logic and architecture validation.
- Configured x64 target architecture, nullable reference types enabled, and treat warnings as errors in Release mode.
- Post-build target in `NJPlayer.App` to automatically copy native binaries from `libs/` to the application output directory.
- Root `.gitignore` for build artifacts, IDE files, and native binaries.
- Documentation and binary drop guide in `libs/README.txt`.
