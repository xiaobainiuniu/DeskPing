# DeskPing

<div align="right">[简体中文](README.md) · **English**</div>

<p align="center">
  <strong>A quiet Windows desktop timer that stays out of the way — until it is time to get your attention.</strong>
</p>

<p align="center">
  Count-up · Countdown · Target Time · Daily Repeat · System Tray · Bilingual UI
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Platform-Windows%20x64-555" alt="Windows x64" />
  <img src="https://img.shields.io/badge/.NET-10-512bd4" alt=".NET 10" />
  <img src="https://img.shields.io/badge/Version-1.8.0-2f81f7" alt="Version 1.8.0" />
  <img src="https://img.shields.io/badge/License-MIT-green" alt="MIT License" />
</p>

## Screenshots

| Countdown | Count-up | Target time |
| :-: | :-: | :-: |
| ![Countdown](docs/screenshots/countdown.png) | ![Count-up](docs/screenshots/countup.png) | ![Target time](docs/screenshots/target.png) |

## Features

| Mode | Description |
| :--- | :--- |
| **Count-up** | Counts upward from zero for focus sessions, commutes, workouts, and more |
| **Countdown** | Enter a duration directly in H:M:S and get alerted at zero |
| **Target time** | Pick a time today, tomorrow, or on a custom date; supports **daily repeat** |

All three modes include a **note box**. While the timer is running, the note appears beneath the time and is included in the alert when the timer fires.

> Once a timer starts, the settings area collapses so the running task stays visually clean. Press **Reset** to configure a new session. Session content is not persisted between launches.

### Alerts

- Window pops up and comes to the front
- Digits bounce and the border pulses
- Taskbar icon flashes
- Optional alert sound
- Tray balloon notification
- Press **Got it** to stop the alert and reset

### Window & tray

- **System tray resident** — double-click to show/hide, right-click for controls
- **Always on top** — available from the pin button or tray menu
- **Resizable** — drag the window edges; timer digits scale with the window
- **Close to tray** — `×` hides the app instead of exiting
- **Persistent preferences** — theme, language, sound, pin state, and related settings are saved

### Language & themes

The app defaults to Chinese and can switch to English instantly from the tray menu.

Themes include **Warm Paper / Ink / Forest / Rose**, with dark variants.

## Power design

- A single **1Hz ticker** drives timer updates; no busy polling
- Idle CPU usage stays near 0%
- No UI refresh while the window is hidden
- Alert animations run only while an alert is active
- On-demand GDI+ drawing with no persistent render pipeline

## Usage

1. Launch `DeskPing.exe`.
2. Choose Count-up, Countdown, or Target time.
3. Optionally add a note and start the timer.
4. Use the tray menu for language, theme, always-on-top, alert sound, auto-start, and exit.

Settings are stored at:

```text
%APPDATA%\DeskPing\settings.json
```

## Build

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```sh
dotnet build -c Release
```

| Script | Output | Description |
| :--- | :--- | :--- |
| `publish.cmd` | `publish\DeskPing.exe` | Smaller build; requires .NET 10 Desktop Runtime on the target PC |
| `publish-self-contained.cmd` | `publish-self-contained\DeskPing.exe` | Self-contained build that runs without a separate runtime install |

## Tech stack

`C#` · `.NET 10` · `WinForms` · `GDI+` · `Windows Tray API`

## License

[MIT](LICENSE)
