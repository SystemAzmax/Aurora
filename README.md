# Aurora Wallpaper Changer

English | [日本語](README_ja.md)

A multi-monitor wallpaper changer for Windows 10 / 11. It runs in the notification area (system tray) and changes the wallpaper of each monitor at a set interval, using images from the folders you choose for that monitor.

## Features

- Choose wallpaper folders for each monitor (subfolders can be included). Supported images: JPG / JPEG / PNG / BMP / WEBP
- Layouts: Single image, 2 × 2 tiles, 4 × 4 tiles, and Span (one image across multiple monitors)
- Fit for a single image: Fill, Fit, Stretch, or Center (can be chosen for each monitor)
- Selection: Random or In order
- Change interval: 1 minute to 24 hours
- Tray menu commands: Next wallpaper, Previous wallpaper, Refresh all wallpapers, Pause, Don't show this image, and Open image location
- Waits to change wallpapers automatically while a full-screen app is running or while on battery power (each can be turned off in Settings)
- Copy the settings of a previous monitor, for example to a new monitor
- Start automatically with Windows
- Export the settings to a file and import them on another PC or after reinstalling
- Japanese and English user interface

## Requirements

- Windows 10 version 1809 or later, or Windows 11 (x64)
- WEBP images require the WebP codec for Windows (built into Windows 11; on Windows 10, install "WebP Image Extensions" from the Microsoft Store)

## Download

The app is being prepared for release on the Microsoft Store. Until then, build it from source (see "For Developers" below).

## Getting Started

1. Start the app. Its icon appears in the notification area.
2. Double-click the icon, or right-click it and select "Settings", to open the Settings window.
3. Select a monitor and add folders of images to "Wallpaper folders".
4. Choose the layout, fit, selection, and change interval.

## Privacy

The app does not collect personal information or send it anywhere. For details, see the [privacy policy](docs/PrivacyPolicy.md).

## Contact

Please report bugs and send requests through [GitHub Issues](https://github.com/SystemAzmax/Aurora/issues).

## Third-Party Software Licenses

See [ThirdPartyNotices.txt](WallpaperChanger.UI/ThirdPartyNotices.txt). You can also open it from "License information" in the app's Settings window.

## For Developers

The app sets wallpapers with the `IDesktopWallpaper` COM API (it does not use `SystemParametersInfo`).

### Project Structure

| Project | Role | Main types |
|---|---|---|
| `WallpaperChanger.Core` (`net10.0`) | OS-independent models, interfaces, and logic | `IWallpaperService` `IMonitorService` `ISettingsService` `IImageProvider` `ITrayIconService` `IWallpaperScheduler` / `WallpaperRotationService` `WallpaperScheduler` |
| `WallpaperChanger.Infrastructure` | Windows-specific implementations | `DesktopWallpaperComHost` (dedicated STA thread) `DesktopWallpaperService` `DesktopWallpaperMonitorService` `JsonSettingsService` `FileSystemImageProvider` |
| `WallpaperChanger.UI` (WinExe) | WPF / MVVM / notification area icon | `Program` (composition root) `SettingsViewModel` `TrayIconViewModel` `TrayIconService` `SettingsWindowService` |
| `WallpaperChanger.Tests` | xUnit v3 tests | Core / Infrastructure / ViewModel / integration tests with the real COM API |
| `WallpaperChanger.Package` (wapproj) | Store (MSIX) package | `Package.appxmanifest`, package images, display name and description (Japanese and English) |

- Views have no code-behind. `InitializeComponent()`, generated from `x:Class`, is called from `SettingsWindowService` and `Program`.
- Data location: `%LOCALAPPDATA%\WallpaperChanger` for the exe version, and `%LOCALAPPDATA%\Packages\<package family name>\LocalState` for the Store version (when running with a package; `AppDataLocation`). Files that a packaged app creates in `%LOCALAPPDATA%` are not visible to other processes, so Explorer, which draws the wallpaper, could not read the combined images. The paths below are for the exe version.
- Automatic startup: the HKCU Run key for the exe version, and the startup task in the manifest for the Store version (`PackagedStartupRegistrationService`)
- Settings file: `%LOCALAPPDATA%\WallpaperChanger\settings.json`
- History file: `%LOCALAPPDATA%\WallpaperChanger\history.json` (the "previous wallpaper" history and the in-order position for each monitor; it is written on every change, so it is kept separate from the settings file)
- App icon: an original icon generated only from shapes by `tools/IconGenerator/generate-icons.cs` (no external assets). To regenerate it, run `dotnet run tools/IconGenerator/generate-icons.cs -- <output folder>` and copy `D-combined.ico` over `WallpaperChanger.UI/Resources/AppIcon.ico`.
- Tiles (2 × 2 / 4 × 4): `IDesktopWallpaper` sets one image per monitor, so `WicWallpaperComposer` combines 4 or 16 images into one image at the monitor's resolution (saved in `%LOCALAPPDATA%\WallpaperChanger\Composites`; the latest 3 images are kept for each monitor). Folders can be set for 4 units (top left, top right, bottom left, bottom right; `MonitorSettings.Tiles`). They apply to each tile in 2 × 2 tiles and to each section of 2 × 2 tiles in 4 × 4 tiles (both layouts share the settings). The fit in tiles (fill / show the whole image), spacing, spacing color, and blank tile and margin color can be set for each monitor (`MonitorSettings.TileStyle`).
- Span (one image across multiple monitors): among the monitors whose layout is set to Span, one image is split to match the position of each monitor (`MonitorInfo.Bounds`). The Windows "Span" setting is not used because it applies to all monitors. Folders, selection, and fit are span-only settings (`AppSettings.Span`), and the span as a whole has one history.
- Conditions for waiting: while a full-screen app is running (`SHQueryUserNotificationState`) or the PC is on battery power (`GetSystemPowerStatus`), scheduled changes wait and run once the condition ends (`AppSettings.SkipConditions`; both are on by default). Manual changes are not blocked.
- Languages: Japanese (default) and English. The strings are in `WallpaperChanger.UI/Resources/Strings.resx` (Japanese) and `Strings.en.resx` (English), and are referenced through the `Strings` class generated at build time (in XAML, `{x:Static res:Strings.Xxx}`). When you add a string, add it to both files with the same key (tests fail if the English string is missing or the placeholders differ). Strings for the UI created in Infrastructure are in `WallpaperChanger.Infrastructure/Resources/Strings.resx`. A language change takes effect after the app restarts, and logs are always written in Japanese.
- App information: "App information", at the end of the Settings window, has "About this app" (app name, version, and copyright; it shows "Store version" when running with a package and "exe version" without one) and "License information" (opens `ThirdPartyNotices.txt` in the default app). They follow the shared rules for apps distributed on the Microsoft Store (StoreReleaseKit). The app name, version, and copyright are in `Directory.Build.props`. How to regenerate the license notices and how to bump the version are in [docs/BuildGuide.md](docs/BuildGuide.md) (in Japanese). The privacy policy is [docs/PrivacyPolicy.md](docs/PrivacyPolicy.md).
- Fit for a single image: by default (`MonitorSettings.SingleImageFit` is null), the image is set as is and shown with the Windows fit setting. Because the Windows fit setting applies to all monitors, when Fill, Fit, Stretch, or Center is chosen for a monitor, `WicWallpaperComposer` (the same one used for tiles) creates an image at the monitor's resolution and sets it (margins are black).
- The README is split into English (`README.md`) and Japanese (`README_ja.md`). When you change it, update both.

### Build, Run, and Test

```powershell
dotnet build Aurora.slnx
dotnet run --project WallpaperChanger.UI
dotnet test --project WallpaperChanger.Tests
# In CI, which has no interactive session, exclude the integration tests that use the real COM API
dotnet test --project WallpaperChanger.Tests -- --filter-not-trait "Category=Integration"
```

On every push to master and every pull request, GitHub Actions (`.github/workflows/ci.yml`) builds the solution in Release from a clean state and runs the tests except the integration tests.

### Publish (Self-contained)

The exe version:

```powershell
dotnet publish WallpaperChanger.UI -p:PublishProfile=exe-win-x64
# => artifacts\publish\win-x64\WallpaperChanger.exe (a single file that includes the .NET runtime)
```

The Store (MSIX) package is created from `WallpaperChanger.Package` in Visual Studio (see "ストア版のビルド" in [docs/BuildGuide.md](docs/BuildGuide.md), in Japanese).
