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

    /// <summary><see cref="ExportAsync"/> で書き出したファイルと、そのときの設定。</summary>
    public List<(string FilePath, AppSettings Settings)> Exported { get; } = [];

    /// <summary><see cref="ImportAsync"/> で読み込むファイルの設定（ファイルのパスごと）。無いパスは <see cref="InvalidDataException"/> にする。</summary>
    public Dictionary<string, AppSettings> ImportFiles { get; } = [];

    public Task ExportAsync(string filePath, CancellationToken cancellationToken = default)
    {
        Exported.Add((filePath, Current));
        return Task.CompletedTask;
    }

    public Task ImportAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!ImportFiles.TryGetValue(filePath, out AppSettings? imported))
        {
            throw new InvalidDataException($"設定ファイルではありません: {filePath}");
        }

        return UpdateAsync(_ => imported, cancellationToken);
    }
}
