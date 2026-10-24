namespace WallpaperChanger.Core.Models;

/// <summary>
/// 保存・復元する 1 モニター分の壁紙履歴。
/// </summary>
/// <param name="MonitorId">モニターの ID（<see cref="MonitorInfo.Id"/>）。</param>
/// <param name="Items">履歴の組（古い順）。</param>
/// <param name="Position">表示中の組の位置（<paramref name="Items"/> の添字）。「前へ」で戻った後は末尾より前になる。</param>
public sealed record WallpaperHistorySnapshot(string MonitorId, IReadOnlyList<WallpaperSelection> Items, int Position);
