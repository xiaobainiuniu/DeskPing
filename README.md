# DeskPing · 一张纸提醒

<div align="center">

**让提醒安静地待在桌面右下角，到点时用力叫醒你。**

一个极简的 Windows 桌面提醒器：正计时 / 倒计时 / 目标时刻，暖纸画风，功耗最低。

![platform](https://img.shields.io/badge/platform-Windows%20x64-555) ![.NET](https://img.shields.io/badge/.NET-10-512bd4) ![UI](https://img.shields.io/badge/UI-WinForms%20自绘-0078d4)

</div>

## 功能

| 模式 | 说明 |
| :--- | :--- |
| **正计时** | 从零开始累计，适合专注时长、通勤、健身等 |
| **倒计时** | 设定时长（如 25 分钟），归零即提醒；带 +5 / +15 / +30 分钟快捷累加 |
| **目标时刻** | 设定时刻（今天 / 明天 / 自定义日期），到点提醒；支持**每日重复** |

### 到点提醒（三重保险，不会错过）

- **纸片跳出** + 时间数字**上下跳动** + 边框呼吸闪动（警示色）
- **任务栏图标闪烁**（系统级 FlashWindowEx）
- **提示音**（可关）+ **托盘气泡**

### 托盘

- 右下角常驻图标，**双击显示纸片，右键菜单退出**
- 菜单可切换模式、主题、深色模式、提醒音、开机自启

## 设计理念

- **一张纸** — 画风完整借鉴 [PaperTodo](https://github.com/snownico0722/PaperTodo) 的暖纸风：同款暖纸 / 墨 / 林 / 霞四套配色（含深色版）、圆角纸片、细描边、弱化文字、系统阴影。
- **功耗最低** — WinForms + GDI+ 自绘，无 WPF 常驻合成器、无后台轮询：
  - 计时核心只有一个 **1Hz 节拍器**，每秒做一次比较，空闲时 CPU 占用 ≈ **0%**；
  - 窗口隐藏时不刷新任何界面；所有绘制按需进行；
  - 跳动动画只在**提醒期间**以 25fps 运行，确认后立即停止；
  - 常驻内存约 30MB 级别，可放心让它一直待在托盘里。

## 使用

1. 下载 / 构建 `DeskPing.exe`（见下文），双击运行。
2. 纸片出现在屏幕右下角；**关闭（×）只是藏进托盘**，不会退出。
3. 右键托盘图标 → 退出，才是真正退出。
4. 设置自动保存在 `%APPDATA%\DeskPing\settings.json`，下次启动恢复。

## 构建

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download)。

```sh
dotnet build -c Release
```

### 发布单文件

| 脚本 | 产物 | 说明 |
| :--- | :--- | :--- |
| `publish.cmd` | `publish\DeskPing.exe` | 约 300KB，需要目标机器安装 .NET 10 桌面运行时 |
| `publish-self-contained.cmd` | `publish-self-contained\DeskPing.exe` | 自带运行时，无需安装任何东西，约 60MB |

## 致谢

画风（配色 / 纸张质感 / 设计语言）借鉴 [PaperTodo](https://github.com/snownico0722/PaperTodo) —— 一个很棒的桌面纸片便签应用。
