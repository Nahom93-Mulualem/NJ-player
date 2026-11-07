---
name: NJ Player
tagline: Fast. Dark. Yours.
colors:
  bg: '#0B0E12'
  surface: '#12171D'
  surface-raised: '#1A212A'
  border: '#252E39'
  text: '#E6EDF3'
  text-muted: '#8B98A5'
  accent: '#00E5FF'
  accent-hover: '#5CF0FF'
  accent-pressed: '#00B8CC'
  on-accent: '#001418'
  danger: '#FF5C6C'
  success: '#3DDC97'
typography:
  ui: {fontFamily: 'Segoe UI Variable Text, Segoe UI, sans-serif', fontSize: 13px, fontWeight: '400'}
  ui-strong: {fontFamily: 'Segoe UI Variable Text, Segoe UI, sans-serif', fontSize: 13px, fontWeight: '600'}
  title: {fontFamily: 'Segoe UI Variable Display, Segoe UI, sans-serif', fontSize: 16px, fontWeight: '600'}
  time: {fontFamily: 'Consolas, Cascadia Mono, monospace', fontSize: 12px, fontWeight: '400'}
rounded: {sm: 4px, md: 8px, lg: 12px, full: 9999px}
spacing: {xs: 4px, sm: 8px, md: 12px, lg: 16px, xl: 24px}
motion:
  fast: 120ms
  base: 180ms
  slow: 280ms
  easing: cubic-bezier(0.22, 1, 0.36, 1)
  controls-autohide: 2500ms
---

# NJ Player Design Spec

## 1. Feel
Borderless, dark, quiet. The video is the hero; the interface appears when needed and disappears when not. Electric cyan is used sparingly, only for what is active or interactive. Nothing decorative, nothing slow.

## 2. Window
- Frameless window (WPF `WindowChrome`) with a 1px `border` outline and a soft shadow when not maximized. Snap layouts, resize edges and taskbar behavior must keep working.
- Top bar (36px, `surface` at 85% opacity): app icon, file title (ellipsis), and window buttons (always on top, minimize, maximize, close). Close hover is `danger`.
- The top bar and bottom controls fade out after `controls-autohide` of mouse inactivity, and fade in on mouse move. The cursor hides with them in fullscreen.
- Empty state: centered logo, "Drop a file or press Ctrl+O", the recent files list.

## 3. Bottom control bar (overlay window)
Height 72px, `bg` gradient from transparent to 92% opaque.
Layout, left to right: Play/Pause, Previous, Next, Stop, time `00:00 / 00:00`, **seekbar (flex)**, Volume (icon + slider), Subtitles, Audio track, Speed, Playlist toggle, Fullscreen.
- Buttons are 36px hit targets, icon-only with tooltips, `text-muted` at rest, `text` on hover, `accent` when toggled on.
- Use one icon set only (Segoe Fluent Icons or Material Symbols), 20px, 1.5px stroke feel.

## 4. Seekbar
- 4px track (`border`), buffered range (`text-muted` at 35%), played range `accent`. On hover the track grows to 8px (`fast`) and a 14px thumb appears.
- **Hover preview:** a 160x90 thumbnail with `md` radius and a time label above the cursor, following the pointer, clamped inside the window. Show the time label immediately and the thumbnail when ready (no blocking).
- Chapters appear as small gaps in the track. Click or drag seeks. Dragging seeks live with fast (non-exact) seeks and does one exact seek on release.
- Keyboard focusable, with a visible accent focus ring.

## 5. Playlist drawer
- Slides in from the right, 340px wide, `surface` with `border` on the left edge, `base` easing. Does not resize the video; it overlays it.
- Rows 44px: thumbnail or icon, title, duration. The current item has an `accent` bar on its left. Drag handle for reordering with a clear drop indicator. Right-click menu: Play, Remove, Show in folder, Properties.
- Search box at the top. Shuffle and repeat toggles at the bottom.

## 6. Panels (equalizer, subtitles, radio, trimmer, settings)
- Modeless side panels or compact dialogs on `surface-raised`, `lg` radius, 1px `border`, Level shadow `0 12px 32px rgba(0,0,0,0.45)`.
- Equalizer: 10 vertical sliders (31 Hz to 16 kHz) with dB labels, presets dropdown, Bass Boost, Treble, Night Mode toggles, Reset.
- Radio: search, country filter (Ethiopia first in the list), station rows with a play button, live indicator, and a real-time audio visualizer when playing.
- Trimmer: timeline with in/out handles, "Set start" / "Set end" buttons (hotkeys I/O), a lossless-mode note, and a Save button.

## 7. Components
- **Primary button:** `accent` fill, `on-accent` text, `md` radius, 32px height, hover `accent-hover`, pressed `accent-pressed`.
- **Secondary button:** `surface-raised` fill, `text` label, 1px `border`.
- **Slider:** same style as the seekbar, smaller.
- **Toggle:** 36x20 pill; on state `accent`.
- **Toast:** bottom-center, `surface-raised`, auto-hide 2s, used for volume %, speed, subtitle delay, seek step, and errors.
- **Context menu:** `surface-raised`, `md` radius, 32px rows, `accent` at 12% opacity on hover.

## 8. Motion
Animate only opacity and transform. Controls fade in `fast`, drawers slide `base`, toasts fade `fast`. Respect Windows "Show animations" being off (skip transitions). No motion while seeking or scrubbing.

## 9. Accessibility
Contrast 4.5:1 or better for text. Every control reachable by keyboard. Tooltips and `AutomationProperties.Name` on all icon buttons. Support 100% to 200% DPI scaling. Optional high-contrast theme later.

## 10. Default hotkeys (all rebindable in `hotkeys.json`)
| Key | Action | Key | Action |
|---|---|---|---|
| Space | Play / Pause | M | Mute |
| Left / Right | Seek -/+ 5 s | Up / Down | Volume +/- 5 |
| Shift+Left / Right | Seek -/+ 1 s | F or Enter | Fullscreen |
| Ctrl+Left / Right | Seek -/+ 30 s | Esc | Exit fullscreen |
| Alt+Left / Right | Previous / next chapter | [ / ] | Speed -/+ 0.1x |
| N / P | Next / previous file | \ | Reset speed |
| Z / X | Subtitle delay -/+ 100 ms | V | Toggle subtitles |
| A | Cycle audio track | S | Screenshot |
| Ctrl+O | Open file | Ctrl+L | Playlist |
| Ctrl+U | Open URL | T | Always on top |
| E | Equalizer | I | Media info |
| Ctrl+Shift+S | Subtitle download | Home / End | Jump to start / end |
| , / . | Frame step back / forward | 0-9 | Jump to 0-90% |

## 11. Voice (text in the UI)
Short, calm, plain. "Couldn't open this file" not "Fatal error". Show what happened and what to try.
