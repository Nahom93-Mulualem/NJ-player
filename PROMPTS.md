# NJ Player: Prompts for Antigravity (send one at a time)

How to use: open the project folder in Antigravity, turn on **Planning mode**, paste one prompt, read the plan, approve, test the result by hand, commit to Git, then continue. If something goes wrong, paste the exact error into the chat.

---

## Prompt 0: Kickoff (send first)
```
Read AGENTS.md, DESIGN.md and everything in .agents/rules/. We are building NJ Player, a Windows-only media player in C# WPF with libmpv, inspired by PotPlayer.
Do NOT write code yet. Summarize back to me in 10 lines: the goals, the stack, the solution layout, and the rules you will follow. Then list any questions or risks (licensing of libmpv/FFmpeg builds, airspace issue with HwndHost, frameless window pitfalls). Wait for my answers.
```

## Prompt 1: Scaffold
```
Create the solution described in AGENTS.md: NJPlayer.sln with projects NJPlayer.Core (class library), NJPlayer.App (WPF app), NJPlayer.Features (class library), NJPlayer.Tests (xUnit). Target the current .NET LTS, x64, nullable enabled, warnings as errors in Release. Add .gitignore (bin, obj, .vs, libs/*.dll, libs/*.exe), a docs/CHANGELOG.md, and a libs/README.txt telling me which binaries to place there. Add a post-build step that copies libs/* next to the exe. Make sure `dotnet build` passes. Show me the tree when done.
```

## Prompt 2: mpv wrapper (Core)
```
In NJPlayer.Core, write a thin libmpv wrapper using P/Invoke (no third-party wrapper): safe handle for the mpv context, options set before initialize, commands via mpv_command with a string array (never concatenated paths), property get/set, property observation, and one event-loop thread that raises .NET events (TimePositionChanged, DurationChanged, PauseChanged, VolumeChanged, TrackListChanged, EndFile, Error). Add clean disposal. Expose a small MpvPlayer class with Load, Play, Pause, Seek(seconds, exact), SetVolume, SetSpeed. Add unit tests for pure logic (option building, event mapping). Explain how threading and disposal work in 10 lines.
```

## Prompt 3: Video host and first playback
```
In NJPlayer.App, create VideoHost : HwndHost that creates a child window and passes its handle to mpv as `wid` before initialize. Create MainWindow with VideoHost filling the window. Add Ctrl+O to open a file and drag-and-drop of files onto the window. Use hwdec=auto-safe and log which hwdec mpv actually selected. Add a temporary debug label showing time-pos and duration. Keep UI minimal; I only want to confirm playback works with hardware decoding. Give me manual test steps.
```

## Prompt 4: Frameless window and overlay controls
```
Implement the frameless window from DESIGN.md section 2 using WindowChrome (resize edges, snap layouts, taskbar, shadow must work). Implement the control overlay as a separate transparent owned window that follows the main window (move, resize, minimize, maximize, fullscreen, multi-monitor, 100-200% DPI), to solve WPF airspace over the mpv child window. Add the auto-hide behavior (2.5 s, cursor hides in fullscreen). Create Themes/Colors.xaml and Themes/Controls.xaml with all tokens from DESIGN.md. For now the overlay has play/pause, time labels and a basic slider. List edge cases you tested or could not test.
```

## Prompt 5: Custom seekbar with hover thumbnails
```
Build the custom seekbar exactly as DESIGN.md section 4 describes: grow-on-hover track, buffered range, live scrubbing with fast seeks and one exact seek on release, chapter gaps, keyboard support. Add hover thumbnail previews: generate single frames with ffmpeg (external process via ArgumentList, low priority, cancellable), debounce 80 ms, cache by 5-second time bucket in memory, show the time label instantly and the thumbnail when ready. Never block the UI thread. Tell me how seek latency feels and add timing logs.
```

## Prompt 6: Hotkeys system
```
Implement the data-driven hotkey system: a CommandId enum, a default map from DESIGN.md section 10, loading and saving hotkeys.json, a conflict check, and a settings page to rebind keys. Include on-screen toasts for volume, speed, seek step and subtitle delay. Add unit tests for the key-to-command mapping and conflict detection.
```

## Prompt 7: Playlist drawer
```
Build the playlist drawer from DESIGN.md section 5: slide-in overlay, drag-and-drop adding of files and folders, drag reordering with a drop indicator, search, shuffle, repeat, remove, show in folder, save and load M3U. Auto-advance on end of file. Remember the last playlist. Use mpv's playlist or our own list; explain your choice before coding.
```

## Prompt 8: Settings, resume, file associations, single instance
```
Add: JSON settings in %APPDATA%\NJPlayer with a portable mode (portable.flag next to the exe); resume position per file; recent files; single-instance behavior (opening a file in a second launch sends it to the running window); command-line arguments (file paths, --bench); window position and size memory; "open with" support. Add a settings window with a clean layout from DESIGN.md. Do not register file associations without my confirmation; just provide the code and explain how the installer will do it.
```

## Prompt 9: Audio suite
```
Add the audio panel: 10-band graphic equalizer (31 Hz to 16 kHz), presets, Bass Boost, Treble, and Night Mode (loudness normalization), using mpv audio filters. Apply changes live without audio glitches, save per-user settings, and add a reset button. Explain which mpv/ffmpeg filters you used and why. Keep the code behind an interface so the feature can be lazy-loaded.
```

## Prompt 10: Subtitles
```
Add subtitle support: load external files, auto-load matching files, track switching, delay (Z/X), size and position, encoding detection, and correct Ge'ez/Amharic rendering with a chosen font. Then design a subtitle downloader behind an ISubtitleProvider interface with a hash-based lookup (compute the file hash the provider requires, off the UI thread). Before choosing a provider, tell me which free keyless services exist today and whether they are active and allowed for this use; do not assume. Build one provider, handle "not found" and network errors gracefully, and show results in a simple picker.
```

## Prompt 11: Trimmer and downloader
```
Add (a) a lossless trimmer using ffmpeg stream copy (-c copy) with I/O hotkeys for in/out points, a progress display, cancel support, and clear messaging that cuts snap to keyframes in stream-copy mode, with an optional re-encode toggle; (b) a downloader using yt-dlp as an external process: paste a URL, choose format, show progress, cancel, and play when done. Make the downloader optional and add a short notice that users are responsible for what they download and must follow site terms. Use ProcessStartInfo.ArgumentList, async output reading, and kill processes on cancel.
```

## Prompt 12: Radio browser and visualizer
```
Add an Online Radio panel. Use the community radio-browser.info API (no API key): confirm the current API server discovery method before coding, cache results, and put Ethiopia (country code ET) at the top of the country filter, with search and favorites. Playing a station uses mpv with a stream URL. Add a real-time audio visualizer (try mpv's lavfi-complex with showcqt/showwaves first; tell me if that is not smooth enough and suggest alternatives). Handle dead streams, reconnects and metadata (ICY title) gracefully. Do not hard-code station URLs; fetch them from the API.
```

## Prompt 13: Packaging and release
```
Run the /release workflow. Produce a self-contained x64 build, an Inno Setup script (per-user install, optional file associations, Start Menu shortcut, uninstaller), a portable ZIP, SHA-256 checksums, THIRD-PARTY-NOTICES.txt listing every bundled binary and its license, a GitHub Actions workflow that builds on tag, and a draft WinGet manifest. Tell me exactly what I must do manually (code signing, uploading, the license decision) and do not upload anything.
```

## Prompt 14: Quality pass and benchmark
```
Run the /review workflow on the whole repo. Then build a benchmark script that measures cold start to first frame, seek latency, CPU and RAM during playback for 5 sample files (H.264 1080p, HEVC 10-bit 4K, HDR, MKV with many tracks, a long audio file) and writes a Markdown table. Also create a manual QA checklist: Windows 10 and 11, NVIDIA/AMD/Intel GPUs, 100-200% DPI, two monitors, fullscreen, broken files, no-network behavior. Fix High issues and list the rest.
```

---

## Handy "rescue" prompts
**Agent drifted:** `Stop. Re-read AGENTS.md and DESIGN.md. List what you changed that I did not ask for, revert it, and continue only with the original task.`

**Bug with error:** `Here is the exact error and the steps to reproduce: <paste>. Find the root cause first, explain it in 5 lines, then make the smallest fix. Do not refactor.`

**Too big a change:** `Split this into smaller steps that each build and run. Show me step 1 only.`

**Performance doubt:** `Measure before changing. Add timing logs, run the benchmark, and show numbers before and after.`
