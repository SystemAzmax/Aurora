namespace WallpaperChanger.Core.Models;

/// <summary>
/// 分割表示の 1 マス分の設定。
/// </summary>
public sealed record TileSettings
{
    /// <summary>このマス専用の壁紙フォルダ。空の場合はモニター共通のフォルダを使う。</summary>
    public IReadOnlyList<string> Folders { get; init; } = [];

    public TileSettings AddFolder(string folder) => this with { Folders = FolderList.Add(Folders, folder) };

    public TileSettings RemoveFolder(string folder) => this with { Folders = FolderList.Remove(Folders, folder) };
}
