========================================================================
NJ Player - External Native Binaries Directory
========================================================================

Place the following 64-bit (x64) Windows binaries directly into this folder:

1. libmpv-2.dll
   - Purpose: Core media player engine loaded via P/Invoke.
   - Source: Download from mpv-player Windows builds (e.g., shinchiro or zhongfly builds from SourceForge/GitHub).
   - License check: Verify whether the build is GPLv3 or LGPLv2.1+ before distribution.

2. ffmpeg.exe
   - Purpose: Used as an external process for extracting video hover preview thumbnails and lossless media trimming.
   - Source: Download official 64-bit Windows static build from FFmpeg download page (e.g., gyan.dev or BtbN builds).

3. ffprobe.exe
   - Purpose: Used for media stream metadata inspection.
   - Source: Included alongside ffmpeg.exe in official FFmpeg distribution archives.

4. yt-dlp.exe
   - Purpose: Optional streaming media downloader invoked as an external process.
   - Source: Download official Windows executable from https://github.com/yt-dlp/yt-dlp/releases.

------------------------------------------------------------------------
Build Integration:
------------------------------------------------------------------------
- These binaries are git-ignored via `.gitignore` to prevent committing heavy binaries.
- The `NJPlayer.App.csproj` project contains an automated post-build target that copies any `.dll` and `.exe` present in this `libs/` folder directly to the build output directory (next to `NJPlayer.App.exe`).
- If you run the project without these binaries yet, the application will build, but media playback will prompt you to place `libmpv-2.dll` in this directory.

