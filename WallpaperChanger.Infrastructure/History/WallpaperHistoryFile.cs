using WallpaperChanger.Core.Models;

namespace WallpaperChanger.Infrastructure.History;

/// <summary>
/// 履歴ファイルの内容（全モニター分）。
/// </summary>
internal sealed record WallpaperHistoryFile(IReadOnlyList<WallpaperHistorySnapshot> Monitors);
