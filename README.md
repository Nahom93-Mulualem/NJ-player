# NJ Player

A Windows-only media player built with C#, WPF, and libmpv.

> **Status:** In development. Features and compatibility are not yet fully verified.

NJ Player aims to provide fast playback and seeking, hardware decoding, a frameless dark interface, and customizable hotkeys.

## Features

**Implemented**
- Add features here only after they work and have been tested.

**Planned**
- Frameless dark interface with electric-cyan accents
- Customizable hotkeys
- Video playback through libmpv
- Optional tools for trimming, downloads, and thumbnails

## Requirements

- Windows 10 or 11, x64
- .NET SDK: [specify the supported version]
- `libmpv-2.dll` and any required FFmpeg or yt-dlp executables

Third-party binaries are not included in this repository. Check their licenses and distribution terms before bundling or redistributing them.

## Build and run

Add the exact commands once the solution builds reliably:

```powershell
dotnet build
```

## Project docs

- [Design](DESIGN.md)
- [Development instructions](AGENTS.md)
- [Changelog](docs/CHANGELOG.md)

## License

License: **not yet decided**. Check the project and dependency licenses before choosing one.
