# Changelog

All notable changes to NJ Player will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **Data-Driven Hotkey System & On-Screen Toasts (`NJPlayer.Core` & `NJPlayer.App`)**:
  - `CommandId`: Complete enum mapping all player actions specified in `DESIGN.md` Section 10 (Space for play/pause, M for mute, arrow keys for seeking/volume, F for fullscreen, [ / ] for speed, Z/X for subtitle delay, etc.).
  - `HotkeyBinding` and `HotkeyManager`: Centralized keyboard gesture normalization, fast command dispatch, conflict detection, and JSON persistence (`%APPDATA%\NJPlayer\hotkeys.json`).
  - `ToastOverlay`: On-screen notification pill positioned bottom-center on `SurfaceRaisedBrush` (`#1A212A`) with 2.0s auto-hide timer, displaying feedback for volume %, speed, seek steps, subtitle delay, and system status.
  - `HotkeySettingsWindow`: Dark dialog on `surface-raised` with 12px corner radius (`lg`), drop shadow, live conflict detection banner, real-time key recording, and "Reset to Defaults".
  - Automated unit tests (`tests/NJPlayer.Tests/HotkeyTests.cs`) covering default spec hotkey coverage, modifier normalization, conflict detection, and JSON serialization (41 total tests passed).
- **Custom Seekbar & Hover Thumbnail Preview (`NJPlayer.App` & `NJPlayer.Core`)**:
  - `PlayerSeekbar`: Custom control matching `DESIGN.md` Section 4 with 4px background track (`BorderBrush`), buffered range track (`TextMutedBrush` at 35%), and played range track (`AccentBrush`).
  - Smooth 120ms grow-on-hover transition enlarging track from 4px to 8px and revealing a 14px cyan thumb.
  - Chapter gaps support rendering visual dividers for chapter markers along the track.
  - Hover preview popup with 160x90 thumbnail frame, 8px corner radius (`md`), shadow, and instant time label clamped inside window bounds.
  - `FfmpegThumbnailService`: Background extraction using external `ffmpeg.exe` via `ProcessStartInfo.ArgumentList` with low priority (`BelowNormal`), cancellation tokens, and 80ms debounce.
  - In-memory 5-second time bucket cache (`(filePath, bucket_N)`) preventing redundant process spawns and disk reads.
  - Live scrubbing with non-exact fast seeks during drag and a single exact seek on release.
  - Real-time seek latency stopwatch logging (`[NJPlayer Seek Latency]`).
  - Full keyboard support on seekbar (Left/Right for -/+5s, Shift+Left/Right for -/+1s, Ctrl+Left/Right for -/+30s) with visible accent focus ring.
  - Automated unit tests (`tests/NJPlayer.Tests/ThumbnailTests.cs`) covering 5-second time bucket logic and cache clearing (32 total tests passed).
- **Frameless Window & Airspace Controls Overlay (`NJPlayer.App`)**:
  - `Themes/Colors.xaml`: All design tokens from `DESIGN.md` (`BgBrush` `#0B0E12`, `SurfaceBrush` `#12171D`, `Surface85Brush` `#D912171D`, `AccentBrush` `#00E5FF`, `DangerBrush` `#FF5C6C`, typography, radii).
  - `Themes/Controls.xaml`: Frameless window caption buttons (with red hover on close), 36px icon buttons, primary buttons, and accent sliders.
  - Frameless `MainWindow` with WPF `WindowChrome` maintaining native Windows snap layouts, resize handles, taskbar integration, and a 1px border outline.
  - `ControlsOverlayWindow`: Transparent owned window solving WPF airspace over the Win32 video child window.
  - Real-time overlay synchronization on window move, resize, minimize, maximize, and DPI changes.
  - 36px Top Bar with app icon, media title, always-on-top pin toggle, and window control buttons.
  - 72px Bottom Bar with playback controls (play/pause, stop), time display, seekbar scrubbing (fast seek while dragging, exact seek on release), volume slider, and fullscreen toggle.
  - 2.5-second auto-hide timer (`controls-autohide`) with 120ms fade in/out transitions (`fast`), auto-hiding the mouse cursor in fullscreen mode.
- **Video Host & Playback Integration (`NJPlayer.App`)**:
  - `VideoHost`: `HwndHost` subclass creating a Win32 child window with `WS_CHILD | WS_CLIPCHILDREN | WS_VISIBLE`, passing its handle (`wid`) to mpv for zero-copy native video rendering.
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
