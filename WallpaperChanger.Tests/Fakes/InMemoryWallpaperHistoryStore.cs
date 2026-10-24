using WallpaperChanger.Core.Interfaces;
using WallpaperChanger.Core.Models;

namespace WallpaperChanger.Tests.Fakes;

/// <summary>
/// メモリ上だけで動作する IWallpaperHistoryStore。
/// 同じインスタンスを新しい WallpaperRotationService に渡すと、アプリの再起動を再現できる。
/// </summary>
internal sealed class InMemoryWallpaperHistoryStore : IWallpaperHistoryStore
{
    public IReadOnlyList<WallpaperHistorySnapshot> Saved { get; private set; } = [];

    public int LoadCount { get; private set; }

    public int SaveCount { get; private set; }

    public Task<IReadOnlyList<WallpaperHistorySnapshot>> LoadAsync(CancellationToken cancellationToken = default)
    {
        LoadCount++;
        return Task.FromResult(Saved);
    }

    public Task SaveAsync(IReadOnlyList<WallpaperHistorySnapshot> histories, CancellationToken cancellationToken = default)
    {
        Saved = histories;
        SaveCount++;
        return Task.CompletedTask;
    }
}
