namespace WallpaperChanger.Core.Models;

/// <summary>
/// 分割表示の 1 区画分の設定（4 分割では 1 マス、16 分割では 2 × 2 の 4 マス）。
/// </summary>
public sealed record TileSettings
{
    /// <summary>この区画専用の壁紙フォルダ。空の場合の扱いは <see cref="MonitorSettings.GetEffectiveTileFolders"/> を参照。</summary>
    public IReadOnlyList<string> Folders { get; init; } = [];

    public TileSettings AddFolder(string folder) => this with { Folders = FolderList.Add(Folders, folder) };

    public TileSettings RemoveFolder(string folder) => this with { Folders = FolderList.Remove(Folders, folder) };
}
