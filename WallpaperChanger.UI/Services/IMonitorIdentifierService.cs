using WallpaperChanger.Core.Models;

namespace WallpaperChanger.UI.Services;

/// <summary>識別表示するモニターと、その番号。</summary>
/// <param name="Number">設定画面の一覧と同じ番号。</param>
/// <param name="Monitor">対象モニター。</param>
/// <param name="IsSelected">設定画面で選択中か。</param>
public sealed record MonitorIdentification(int Number, MonitorInfo Monitor, bool IsSelected);

/// <summary>
/// 各モニターの画面上に番号を一定時間表示し、一覧の番号と実際のモニターを対応付けられるようにする。
/// </summary>
public interface IMonitorIdentifierService
{
    /// <summary>表示する時間。</summary>
    TimeSpan DisplayDuration { get; }

    /// <summary>
    /// 番号を表示し、<see cref="DisplayDuration"/> 経過後に閉じる。表示中に再度呼ばれた場合は表示し直す。
    /// UI スレッドから呼び出すこと。
    /// </summary>
    Task ShowAsync(IReadOnlyList<MonitorIdentification> monitors, CancellationToken cancellationToken = default);
}
