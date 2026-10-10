using WallpaperChanger.Core.Interfaces;
using WallpaperChanger.Core.Models;

namespace WallpaperChanger.Tests.Fakes;

/// <summary>
/// メモリ上だけで動作する ISettingsService。
/// </summary>
internal sealed class InMemorySettingsService(AppSettings? initial = null) : ISettingsService
{
    public AppSettings Current { get; private set; } = initial ?? new AppSettings();

    public int UpdateCount { get; private set; }

    public string? LoadWarning { get; set; }

    public event EventHandler<AppSettings>? SettingsChanged;

    public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task UpdateAsync(Func<AppSettings, AppSettings> update, CancellationToken cancellationToken = default)
    {
        Current = update(Current);
        UpdateCount++;
        SettingsChanged?.Invoke(this, Current);
        return Task.CompletedTask;
    }
}
