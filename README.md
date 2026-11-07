# NJ Player Starter Kit

A Windows media player (C# WPF + libmpv) inspired by PotPlayer: frameless dark UI, instant seeking, granular hotkeys.

## What is in this kit
| File | Purpose |
|---|---|
| `AGENTS.md` | Standing instructions the coding agent reads automatically |
| `DESIGN.md` | Look, feel, motion and hotkey spec for the player UI |
| `PROMPTS.md` | Copy-paste prompts, in build order (Prompt 0 to 12) |
| `.agents/rules/*.md` | Topic rules (architecture, C#/WPF, performance) |
| `.agents/workflows/*.md` | Slash-command workflows: /new-feature, /review, /release |
| `.antigravityignore` | Keeps binaries and build output out of the agent's context |

## How to use it with Google Antigravity
1. Create a folder `NJPlayer`, copy everything from this kit into it, and open that folder as your workspace.
2. Antigravity reads `AGENTS.md` and `.agents/rules/` automatically. (Folder names and features change between versions. If rules are not picked up, check the current Antigravity docs, and note that older versions used `.agent/` instead of `.agents/`.)
3. Use **Planning mode** for every prompt in `PROMPTS.md`. Read the plan before approving.
4. Send **one prompt at a time**. Run the app, test, commit to Git, then send the next prompt.
5. If the agent drifts, say: "Re-read AGENTS.md and DESIGN.md, then fix only what I asked."

## What to install (Windows 10/11, x64)
**Required**
1. **Git** (git-scm.com), then run `git config --global user.name` and `user.email`.
2. **.NET SDK**, current LTS (dotnet.microsoft.com). Check with `dotnet --version`.
3. **Visual Studio Community** with the ".NET desktop development" workload (best for the WPF designer and debugger), or VS Code with the C# Dev Kit. Visual Studio is easier for WPF.
4. **Google Antigravity** (antigravity.google) and sign in.
5. **libmpv** (`libmpv-2.dll`, x64): download a "libmpv / mpv-dev" Windows build from the mpv-player/mpv Windows builds list (for example the shinchiro or zhongfly builds). **Check whether the build is GPL or LGPL** before choosing your license.
6. **FFmpeg** (`ffmpeg.exe`, `ffprobe.exe`), from the FFmpeg download page's Windows builds. Again check GPL vs LGPL.
7. **yt-dlp** (`yt-dlp.exe`) from the yt-dlp GitHub releases.

Put the three binaries in `libs/` (not committed to Git).

**For release time**
8. **Inno Setup** (jrsoftware.org) for the installer.
9. **GitHub account** and optionally GitHub CLI (`gh`).
10. A code-signing option (SignPath for open source, or Azure Trusted Signing). Optional at first.

**Useful extras**: Windows Terminal, DiffMerge or VS built-in diff, a few test videos (H.264, HEVC 10-bit, HDR, MKV with several tracks, a broken file), and test audio files.

## Before you publish
- Decide the license (GPL if you bundle GPL libmpv/FFmpeg, MIT only if all bundled parts allow it).
- Check the name "NJ Player" is free on GitHub and trademark searches.
- Verify the subtitle source is still alive and allowed for this use.
- Keep the downloader optional and make the user responsible for what they download.
