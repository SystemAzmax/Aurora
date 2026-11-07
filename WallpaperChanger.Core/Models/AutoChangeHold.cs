namespace WallpaperChanger.Core.Models;

/// <summary>自動切り替えを待っている理由（<see cref="SkipConditions"/> のどれに当てはまっているか）。</summary>
public enum AutoChangeHold
{
    /// <summary>待っていない。</summary>
    None = 0,

    /// <summary>全画面表示のアプリやプレゼンテーションの実行中。</summary>
    FullScreen = 1,

    /// <summary>バッテリーで動作中。</summary>
    OnBattery = 2,
}
