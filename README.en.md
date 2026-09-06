# DeskPing

<div align="right">[简体中文](README.md) · **English**</div>

A tiny Windows tray timer that sits quietly in the corner and loudly wakes you up when time is up.

Count-up / countdown / target time, tray resident, always-on-top, freely resizable, paper-style UI, ultra low power.

![platform](https://img.shields.io/badge/platform-Windows%20x64-555) ![.NET](https://img.shields.io/badge/.NET-10-512bd4)

## Screenshots

| Countdown | Count-up | Target time |
| :-: | :-: | :-: |
| ![Countdown](docs/screenshots/countdown.png) | ![Count-up](docs/screenshots/countup.png) | ![Target time](docs/screenshots/target.png) |

## Features

| Mode | Description |
| :--- | :--- |
| **Count-up** | Counts up from zero — for focus sessions, commutes, workouts |
| **Countdown** | Type a duration (H:M:S); alert at zero |
| **Target time** | Pick a moment (today / tomorrow / custom date); supports **daily repeat** (auto-schedules to the next day when it fires) |

All three modes share a **note box** below the time: click it and type a line (e.g. "drink water", "20-min nap"); when not editing, the text sits centered and dimmed. Once the timer runs, the note appears under the digits as a reminder of what you're doing; when time is up it shows in the balloon and on the alert screen.

> Once the timer starts, the settings area hides automatically — only the time shows; press Reset to bring it back. Session contents (note / durations / target) are not saved, so every launch starts fresh.

### Alert (multi-layer, hard to miss)

- Window pops up on top, digits bounce, border breathes
- Taskbar icon flashes + sound (optional) + tray balloon
- Click "Got it" to stop the animation and reset

### Window & tray

- Lives in the tray; double-click toggles, right-click menu to exit
- Pin button or tray menu to keep it on top
- Drag any corner to resize; digits scale along
- First click on an input selects all for overwrite; second click drops the caret where you click
- **× only hides to tray**, it never quits; app settings auto-save, session contents reset on every launch

### Language

The app defaults to Chinese; switch to English any time via the tray menu **Language**. Takes effect instantly and is saved.

## Power design

- One **1Hz ticker** for the whole timer; idle CPU ≈ 0%, no busy polling
- **Zero UI refresh** while hidden; the bounce animation runs only during the alert
- On-demand GDI+ drawing, no persistent render pipeline

## Usage

1. Run `DeskPing.exe`; the paper card appears at the bottom-right.
2. Right-click the tray icon for mode, language, theme (Warm Paper / Ink / Forest / Rose + dark), pin, sound, auto-start, exit.
3. Settings are stored in `%APPDATA%\DeskPing\settings.json`.

## Build

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```sh
dotnet build -c Release
```

| Script | Output | Description |
| :--- | :--- | :--- |
| `publish.cmd` | `publish\DeskPing.exe` | ~300KB, needs the .NET 10 Desktop Runtime installed |
| `publish-self-contained.cmd` | `publish-self-contained\DeskPing.exe` | Self-contained, no install, ~60MB |
