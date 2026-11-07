namespace WallpaperChanger.Core.Interfaces;

/// <summary>
/// 自動切り替えを見合わせるかの判断に使う、PC の今の状態を取得する。
/// どちらも状態を取得できなければ false を返し、例外は投げない（切り替えを止めたままにしないため）。
/// </summary>
public interface ISystemStateProvider
{
    /// <summary>全画面表示のアプリ（ゲーム・動画など）やプレゼンテーションを実行中か。</summary>
    bool IsFullScreenAppRunning();

    /// <summary>バッテリーで動作しているか（AC 電源につながっていないか）。バッテリーの無い PC では false。</summary>
    bool IsOnBatteryPower();
}
