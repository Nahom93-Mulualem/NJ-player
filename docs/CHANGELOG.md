# Changelog

All notable changes to NJ Player will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Initial project scaffolding for NJ Player solution (`NJPlayer.sln`):
  - `src/NJPlayer.Core`: C# class library for libmpv wrapper, playback engine, settings, and hotkey management.
  - `src/NJPlayer.App`: WPF application for dark frameless video player and transparent overlay interface.
  - `src/NJPlayer.Features`: C# class library for secondary features (subtitles, trimmer, downloader, radio, equalizer).
  - `tests/NJPlayer.Tests`: xUnit test project for core logic and architecture validation.
- Master branding and multi-resolution Windows icon suite in `src/NJPlayer.App/Assets`:
  - `app.ico` containing 16x16, 32x32, 48x48, and 256x256 32-bit RGBA frames for Windows Shell compatibility.
  - Individual resolution PNG exports and master `logo.png`.
  - Configured `ApplicationIcon` in `NJPlayer.App.csproj` and window icon in `MainWindow.xaml`.
- Configured x64 target architecture, nullable reference types enabled, and treat warnings as errors in Release mode.
- Post-build target in `NJPlayer.App` to automatically copy native binaries from `libs/` to the application output directory.
- Root `.gitignore` for build artifacts, IDE files, and native binaries.
- Documentation and binary drop guide in `libs/README.txt`.
