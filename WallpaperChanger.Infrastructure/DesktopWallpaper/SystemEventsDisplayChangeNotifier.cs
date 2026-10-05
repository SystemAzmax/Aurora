using Microsoft.Win32;
using WallpaperChanger.Core.Interfaces;

namespace WallpaperChanger.Infrastructure.DesktopWallpaper;

/// <summary>
/// SystemEvents.DisplaySettingsChanged（WM_DISPLAYCHANGE）を中継する。
/// モニターの接続・切断、解像度・配置の変更で発生する。
/// </summary>
internal sealed class SystemEventsDisplayChangeNotifier : IDisplayChangeNotifier, IDisposable
{
    public SystemEventsDisplayChangeNotifier()
    {
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    public event EventHandler? DisplaysChanged;

    public void Dispose() => SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => DisplaysChanged?.Invoke(this, EventArgs.Empty);
}
