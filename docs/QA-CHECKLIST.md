# NJ Player Manual QA Verification Checklist

This document defines the comprehensive manual test protocol for quality assurance passes prior to release builds.

---

## 1. Operating System & Platform Matrix
- [ ] **Windows 11 (23H2 / 24H2)**: Frameless WindowChrome, Snap Layouts hover on maximize button, Mica/Acrylic visual cohesion, taskbar autohide compatibility.
- [ ] **Windows 10 (21H2 / 22H2)**: Standard dark chrome, window border rendering, taskbar grouping.
- [ ] **x64 Architecture**: Verify native 64-bit address space, single-process execution, no WOW64 emulation.

---

## 2. Hardware Decoding & GPU Engine
- [ ] **NVIDIA GeForce (NVDEC / CUVID)**: Verify `hwdec=auto-safe` resolves to `nvdec` or `d3d11va-copy`. Smooth 4K 60fps / 10-bit HEVC.
- [ ] **AMD Radeon (VCN / D3D11VA)**: Verify `d3d11va` hardware decoding without green frames or block artifacts.
- [ ] **Intel Graphics (QuickSync / Iris Xe / Arc)**: Verify AV1 / HEVC hardware decode without dropped frames.
- [ ] **Software Fallback (`hwdec=no`)**: Ensure CPU decode fallback functions cleanly when GPU acceleration is unavailable.

---

## 3. High DPI Scaling & Multi-Monitor Transitions
- [ ] **100% (96 DPI)**: Standard desktop display; controls sharp, fonts legible, icons pixel-aligned.
- [ ] **125% & 150% DPI**: Common Windows laptop scaling; verify ControlsOverlayWindow correctly aligns with video surface without 1px drift.
- [ ] **175% & 200% DPI**: 4K scaling; ensure seekbar hover thumbnail clamps properly inside visible window boundaries.
- [ ] **Dual/Multi-Monitor**:
  - Drag window from Monitor 1 (100% DPI) to Monitor 2 (150% DPI).
  - Verify zero lag in overlay repositioning (`WM_WINDOWPOSCHANGED`).
  - Maximize on secondary monitor; ensure fullscreen stays bounded to active display.

---

## 4. Window State & Airspace Workaround
- [ ] **Window Dragging**: Drag from top caption bar. Verify smooth Win32 `ReleaseCapture()` + `WM_NCLBUTTONDOWN` behavior without stutter.
- [ ] **Snap Layouts**: Hover over maximize button `⬜` or drag to screen corners/edges. Windows snap grid triggers and window docks accurately.
- [ ] **Controls Autohide**: Inactivity for 2.5 seconds fades out top bar and bottom control bar. Moving cursor instantly brings them back.
- [ ] **Fullscreen Mode (<kbd>F</kbd> / <kbd>Enter</kbd> / Double-Click)**:
  - Taskbar is completely covered.
  - Controls fade out after 2.5s and cursor hides.
  - Moving cursor reveals controls and unhides cursor.
  - <kbd>Esc</kbd> or <kbd>F</kbd> exits back to previous window dimensions.

---

## 5. Playback & Codec Stress Tests
- [ ] **Standard Video (H.264 / AAC MP4)**: Instant startup (< 280ms), audio/video synchronized.
- [ ] **High-Bitrate 4K HEVC 10-bit MKV**: Instant scrub seeking, no frame buffer stalling.
- [ ] **HDR10 (BT.2020 PQ)**: Accurate tone-mapping to SDR display or native HDR passthrough.
- [ ] **Audio-Only (FLAC / MP3 / OGG)**: Empty video canvas displays electric cyan audio waveform or spectrum visualizer.
- [ ] **Corrupted / Broken Files**:
  - Open a 0-byte file: friendly notification, player stays stable.
  - Open a truncated MP4 file: recovers available index, plays playable duration.
  - Open non-media file (.exe, .zip): displays calm notification without application crash.

---

## 6. Offline & No-Network Resilience
- [ ] **Airplane Mode / Disconnected Network**:
  - Application starts instantly with zero hangs.
  - Subtitle Search: shows friendly "Unable to contact subtitle provider (Offline)" message.
  - Online Downloader: displays connection error without crashing UI thread.
  - Online Radio: falls back seamlessly to `%APPDATA%\NJPlayer\radio_cache.json` cached station list.
- [ ] **Dead Radio Stream / Network Interruption**:
  - Player attempts reconnect with 15s timeout.
  - Reports stream disconnect gracefully without unhandled exception.

---

## 7. Integrated Feature Panels
- [ ] **Seekbar & Thumbnails**: Scrubbing seeks smoothly; hovering over timeline displays 80ms debounced thumbnail preview and timestamp.
- [ ] **Playlist Drawer (<kbd>Ctrl+L</kbd> / `📋`)**: Slides in from right (340px); drag-and-drop file/folder addition; drag reordering; M3U import/export.
- [ ] **10-Band Equalizer (<kbd>E</kbd> / `🎚`)**: Live filter updates without audio popping; Bass Boost and Treble adjustments; Night Mode loudness normalization; Reset button restores 0 dB flat line.
- [ ] **Subtitle Picker (`💬`)**: Sidecar auto-load; Ge'ez/Amharic Unicode font rendering; delay adjustment (<kbd>Z</kbd>/<kbd>X</kbd>); online hash lookup.
- [ ] **Lossless Trimmer (`✂`)**: In (<kbd>I</kbd>) and Out (<kbd>O</kbd>) points; lossless `ffmpeg -c copy` export with keyframe snapping notification; optional frame-accurate re-encode toggle.
- [ ] **Online Downloader (`📥`)**: Format dropdown selection; paste URL; legal disclaimer visibility; async download progress; auto-play upon completion.
- [ ] **Online Radio (`📻`)**: Ethiopia (ET) pinned at index 0 in country filter; live ICY stream titles; real-time audio visualizer spectrum (`showcqt`) and oscilloscope (`showwaves`).
- [ ] **Hotkeys Settings (`⌨`)**: Rebindable key shortcuts saved to `hotkeys.json` with duplicate conflict detection.
- [ ] **Application Settings (`⚙`)**: Remember window position; resume playback position toggle; portable storage indicator; clear cache.

