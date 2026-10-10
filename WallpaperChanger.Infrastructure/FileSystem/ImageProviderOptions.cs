namespace WallpaperChanger.Infrastructure.FileSystem;

/// <summary>
/// 壁紙フォルダの列挙結果のキャッシュに関する設定。
/// 切り替えのたびにフォルダ全体を列挙し直すと、大量の画像を含むフォルダやネットワーク共有で負荷が大きいため、結果を使い回す。
/// </summary>
public sealed class ImageProviderOptions
{
    /// <summary>
    /// 列挙結果を使い回す最長の時間。フォルダの監視（<see cref="WatchForChanges"/>）が使えない・止まった場合でも、
    /// この時間が経てば列挙し直して、追加・削除された画像を反映する。
    /// </summary>
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// フォルダを監視し、ファイルの追加・削除・名前の変更があればすぐに列挙し直すか。テストでは無効にできる。
    /// </summary>
    public bool WatchForChanges { get; set; } = true;
}
