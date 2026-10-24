using WallpaperChanger.Core.Models;

namespace WallpaperChanger.Core.Interfaces;

/// <summary>
/// 壁紙の履歴（「前の壁紙」と順送りの位置）を、アプリを再起動しても残るよう保存する。
/// </summary>
public interface IWallpaperHistoryStore
{
    /// <summary>
    /// 保存した全モニターの履歴を読み込む。保存先が無い・読めない・壊れている場合は空を返す（例外にしない）。
    /// </summary>
    Task<IReadOnlyList<WallpaperHistorySnapshot>> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 全モニターの履歴を保存する（保存済みの内容は置き換える）。
    /// 履歴は補助的な情報のため、保存に失敗してもログに記録するだけで例外にしない。
    /// </summary>
    Task SaveAsync(IReadOnlyList<WallpaperHistorySnapshot> histories, CancellationToken cancellationToken = default);
}
