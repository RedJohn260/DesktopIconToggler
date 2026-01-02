# Desktop Icon Toggler 🖥️✨

![Banner](Images/Banner.png)

A modern, ultra-lightweight Windows system tray application that allows you to toggle your desktop icons on and off with a single hotkey. Built with C# and .NET 10.0 for maximum performance.

---

## 🚀 Features

* **Global Hotkey:** Toggle icons instantly from anywhere (Default: `Ctrl + D`).
* **Minimal Footprint:** Uses Native AOT-style optimizations to keep RAM usage under 25MB.
* **Smart Startup:** Optionally launches with Windows so your desktop is always clean.
* **Auto-Updates:** Automatically checks GitHub for new versions so you're always up to date.
* **Zero Install:** A single portable `.exe` file—no installers, no clutter.

## 🛠️ Setup & Installation

1.  **Download:** Head over to the [Releases](https://github.com/RedJohn260/DesktopIconToggler/releases) page.
2.  **Run:** Launch `DesktopIconToggler.exe`.
3.  **Use:** Right-click the icon in your system tray to configure hotkeys or enable "Run at Startup".

## ⌨️ Configuration

| Action | Description |
| :--- | :--- |
| **Right-Click Tray** | Access settings, toggle manually, or check for updates. |
| **Change Hotkey** | Open the settings GUI to record a new key combination. |
| **Toggle Icons** | Press your configured hotkey (Default: `Ctrl + D`). |

## 🏗️ Development

This project was developed using **Visual Studio 2026 Community**.

### Prerequisites
* .NET 10.0 SDK
* Visual Studio 2026 (with .NET Desktop Development workload)

### Compiling
To build the optimized single-file version yourself:
```bash
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:PublishReadyToRun=true