namespace WallpaperChanger.Core.Interfaces;

/// <summary>
/// 設定された間隔で壁紙を自動的に切り替える。
/// </summary>
public interface IWallpaperScheduler : IAsyncDisposable
{
    /// <summary>自動切り替えが動作中か。</summary>
    bool IsRunning { get; }

    /// <summary>一時停止中か。</summary>
    bool IsPaused { get; }

    /// <summary>現在の切り替え間隔。</summary>
    TimeSpan Interval { get; }

    /// <summary>一時停止状態や間隔が変化したときに発生する。</summary>
    event EventHandler? StateChanged;

    /// <summary>定期実行を開始する。</summary>
    void Start();

    /// <summary>定期実行を停止し、実行中の処理の完了を待つ。</summary>
    Task StopAsync();

    /// <summary>自動切り替えを一時停止する（状態は設定に保存される）。</summary>
    Task PauseAsync(CancellationToken cancellationToken = default);

    /// <summary>自動切り替えを再開する（状態は設定に保存される）。</summary>
    Task ResumeAsync(CancellationToken cancellationToken = default);

    /// <summary>即時に次の壁紙へ切り替え、次回の自動切り替えまでの待ち時間をリセットする。</summary>
    Task ChangeNowAsync(CancellationToken cancellationToken = default);
}
