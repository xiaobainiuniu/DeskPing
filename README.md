# DeskPing

**安静待在右下角，到点时用力叫醒你的 Windows 计时提醒器。**

正计时 / 倒计时 / 目标时刻，托盘常驻、置顶、自由缩放，纸张质感界面，功耗极低。

A tiny Windows tray timer that sits quietly in the corner and loudly wakes you up when time is up.

Count-up / countdown / target time, tray resident, always-on-top, freely resizable, paper-style UI, ultra low power.

![platform](https://img.shields.io/badge/platform-Windows%20x64-555) ![.NET](https://img.shields.io/badge/.NET-10-512bd4)

## 功能 Features

| 模式 Mode | 说明 Description |
| :--- | :--- |
| **正计时 Count-up** | 从零开始累计，适合专注时长、通勤、健身等 / Counts up from zero, for focus sessions, commutes, workouts |
| **倒计时 Countdown** | 设定时长（时:分:秒输入 + `+5分/+5m` `+15分/+15m` `+30分/+30m` 快捷累加），归零即提醒 / Set a duration and get alerted at zero |
| **目标时刻 Target time** | 设定时刻（今天 / 明天 / 自定义日期），到点提醒；支持**每日重复**（到点后自动顺延一天）/ Pick a moment (today / tomorrow / custom date); supports **daily repeat** |

### 到点提醒 Alert (multi-layer, hard to miss)

- 窗口自动弹出置顶，数字**上下跳动**，边框**呼吸闪动** — Window pops up on top, digits bounce, border breathes
- **任务栏图标闪烁** + 提示音（可关）+ 托盘气泡 — Taskbar icon flashes + sound (optional) + tray balloon
- 点「知道了」停止动画并复位 — Click "Got it" to stop and reset

### 窗口与托盘 Window & tray

- **托盘常驻**：双击图标显示 / 隐藏，右键菜单退出 — Lives in the tray; double-click toggles, right-click menu to exit
- **置顶**：标题栏图钉按钮或托盘菜单 — Pin button or tray menu to keep it on top
- **自由缩放**：拖动窗口边角即可放大缩小，数字随窗口自动缩放 — Drag any corner to resize; digits scale along
- **点击输入框自动全选**，直接输入新数字覆盖；再点一次光标落到末尾 — First click selects all for overwrite; second click drops the caret at the end
- **× 只是藏进托盘**，不退出；设置自动保存，重启恢复 — × only hides to tray; settings auto-save and restore

### 语言 Language

默认中文，可在托盘菜单 **语言 / Language** 中切换为 English，即时生效并保存。

Chinese by default; switch to English any time via the tray menu **语言 / Language**. Takes effect instantly and is saved.

## 功耗设计 Power design

- 计时核心只有一个 **1Hz 节拍器**，每秒一次比较，空闲 CPU ≈ 0%，无忙轮询 — One 1Hz ticker only; near-zero idle CPU, no busy polling
- 窗口隐藏时**零 UI 刷新**；跳动动画仅提醒期间运行，确认即停 — Zero UI refresh while hidden; animation runs only during the alert
- 全部界面 GDI+ 按需自绘，无常驻渲染管线 — On-demand GDI+ drawing, no persistent render pipeline

## 使用 Usage

1. 运行 `DeskPing.exe`，纸片出现在屏幕右下角。Run `DeskPing.exe`; the paper card appears at the bottom-right.
2. 右键托盘图标可切换模式、语言、主题（暖纸 / 墨 / 林 / 霞 + 深色）、置顶、提醒音、开机自启、退出。Right-click the tray icon for mode, language, theme (Warm Paper / Ink / Forest / Rose + dark), pin, sound, auto-start, exit.
3. 设置保存在 `%APPDATA%\DeskPing\settings.json`。Settings are stored in `%APPDATA%\DeskPing\settings.json`.

## 构建 Build

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download)。Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```sh
dotnet build -c Release
```

| 脚本 Script | 产物 Output | 说明 Description |
| :--- | :--- | :--- |
| `publish.cmd` | `publish\DeskPing.exe` | 约 300KB，需目标机器装有 .NET 10 桌面运行时 / ~300KB, needs the .NET 10 Desktop Runtime |
| `publish-self-contained.cmd` | `publish-self-contained\DeskPing.exe` | 自带运行时，免安装，约 60MB / Self-contained, no install, ~60MB |
