# NJ Player: Agent Instructions

## Project
NJ Player is a free, open-source **Windows-only** video/audio player. Goals: fast startup, instant seeking, hardware decoding, a frameless dark "electric cyan" UI, granular hotkeys. Reference feel: PotPlayer.

## Stack
- C# on current .NET LTS, **WPF** (not WinUI 3), x64 only.
- **libmpv** (`libmpv-2.dll`) loaded via P/Invoke. We write our own thin wrapper. No third-party mpv wrapper libraries unless I approve.
- FFmpeg/ffprobe for thumbnails and lossless trimming. yt-dlp for downloads. Called as external processes.
- Video is shown by giving mpv a child window handle (`wid`) inside an `HwndHost`. Controls are in a separate transparent overlay window (WPF "airspace" workaround).

## Solution layout
```
NJPlayer.sln
src/NJPlayer.Core/       mpv wrapper, playback state, settings, hotkeys, no UI code
src/NJPlayer.App/        WPF windows, views, styles, view models
src/NJPlayer.Features/   subtitles, trimmer, downloader, radio, equalizer
tests/NJPlayer.Tests/
libs/                    libmpv-2.dll, ffmpeg.exe, ffprobe.exe, yt-dlp.exe (git-ignored)
docs/
```

## Always
1. Read `DESIGN.md` before any UI work. Use its tokens (colors, spacing, motion). Never hard-code colors in views; use shared resource dictionaries.
2. Work in small steps. Before editing, show a short plan: files to change and why.
3. After each task: build with `dotnet build`, run tests, and tell me how to test by hand.
4. Keep `Core` free of WPF references.
5. Never block the UI thread. Do file, network and process work asynchronously with cancellation.
6. Handle failure: missing DLL, unsupported file, no network, closed pipe. Show a friendly message, never a crash.
7. Do not add NuGet packages without asking, and state the package license.
8. Do not commit binaries, keys or user data.

## Never
- Never use string concatenation to build mpv commands from file paths. Use `mpv_command` with a string array.
- Never call mpv functions from multiple threads without synchronization.
- Never poll in a tight loop. Use mpv property observation and events.
- Never invent license, benchmark or compatibility claims. Say "not verified".

## Definition of done
Builds with no warnings, tested manually on a sample file, no new memory or handle leaks (dispose mpv handles and processes), UI matches `DESIGN.md`, short note added to `docs/CHANGELOG.md`.
