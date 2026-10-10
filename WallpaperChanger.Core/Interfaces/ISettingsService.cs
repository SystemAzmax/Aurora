using WallpaperChanger.Core.Models;

namespace WallpaperChanger.Core.Interfaces;

/// <summary>
/// アプリケーション設定の読み込み・保存を行う。
/// 設定は不変オブジェクトで、変更は <see cref="UpdateAsync"/> を通じてのみ行う。
/// </summary>
public interface ISettingsService
{
    /// <summary>現在の設定。<see cref="LoadAsync"/> 前は既定値。</summary>
    AppSettings Current { get; }

    /// <summary>
    /// 直前の <see cref="LoadAsync"/> で設定ファイルを読めず既定値を使った場合の、ユーザー向けの説明。問題が無ければ null。
    /// </summary>
    string? LoadWarning { get; }

    /// <summary>設定が変更された（保存された）ときに発生する。</summary>
    event EventHandler<AppSettings>? SettingsChanged;

    /// <summary>
    /// 保存先から設定を読み込む。ファイルが無い・読めない・壊れている場合は既定値を使用する
    /// （読めない・壊れている場合は <see cref="LoadWarning"/> に理由を設定する）。
    /// </summary>
    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>設定を変更して保存する。</summary>
    Task UpdateAsync(Func<AppSettings, AppSettings> update, CancellationToken cancellationToken = default);
}
