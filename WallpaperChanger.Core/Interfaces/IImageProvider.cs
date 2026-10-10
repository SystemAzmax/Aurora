namespace WallpaperChanger.Core.Interfaces;

/// <summary>
/// フォルダから壁紙候補の画像を列挙する。
/// </summary>
public interface IImageProvider
{
    /// <summary>
    /// 指定フォルダ内の対応画像をフルパスで取得する。結果はパス順（大文字小文字無視）に整列済み。
    /// 実装は列挙結果をキャッシュしてよい（その後に削除された画像を含むことがある）。
    /// </summary>
    Task<IReadOnlyList<string>> GetImagesAsync(
        IEnumerable<string> folders,
        bool includeSubfolders,
        CancellationToken cancellationToken = default);

    /// <summary>画像ファイルが存在し、対応形式であるか。</summary>
    bool Exists(string imagePath);

    /// <summary>キャッシュした列挙結果を破棄し、次回の <see cref="GetImagesAsync"/> で列挙し直させる。</summary>
    void Invalidate();
}
