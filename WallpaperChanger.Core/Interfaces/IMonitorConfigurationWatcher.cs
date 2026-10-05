using WallpaperChanger.Core.Models;

namespace WallpaperChanger.Core.Interfaces;

/// <summary>
/// モニター構成の変化を監視し、壁紙を新しい構成に合わせる。
/// 新しく接続されたモニターには壁紙を表示し、解像度が変わった分割表示のモニターは合成し直す。
/// </summary>
public interface IMonitorConfigurationWatcher
{
    /// <summary>モニター構成が実際に変化したときに発生する（任意のスレッド）。</summary>
    event EventHandler<MonitorConfigurationChangedEventArgs>? ConfigurationChanged;

    /// <summary>現在の構成を記録して監視を始める。</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>監視を止め、処理中の更新の完了を待つ。</summary>
    Task StopAsync();
}
