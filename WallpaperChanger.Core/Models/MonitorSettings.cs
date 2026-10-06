namespace WallpaperChanger.Core.Models;

/// <summary>
/// モニター単位の壁紙設定。不変オブジェクトとして扱い、変更は with 式で行う。
/// </summary>
public sealed record MonitorSettings
{
    /// <summary>対象モニターの ID（<see cref="MonitorInfo.Id"/>）。</summary>
    public required string MonitorId { get; init; }

    /// <summary>壁紙フォルダ一覧（1 枚表示、および分割表示でフォルダ未設定のマスで使う）。</summary>
    public IReadOnlyList<string> Folders { get; init; } = [];

    /// <summary>サブフォルダも検索するか。</summary>
    public bool IncludeSubfolders { get; init; } = true;

    /// <summary>次の壁紙の選び方。</summary>
    public ImageSelectionMode SelectionMode { get; init; } = ImageSelectionMode.Random;

    /// <summary>1 枚表示か分割表示（4 分割・16 分割）か。</summary>
    public WallpaperLayout Layout { get; init; } = WallpaperLayout.SingleImage;

    /// <summary>
    /// 分割表示の区画ごとの設定（左上・右上・左下・右下の順）。足りない分は既定値として扱う。
    /// 4 分割では各マス、16 分割では各区画（2 × 2 の 4 マス）に使う。
    /// </summary>
    public IReadOnlyList<TileSettings> Tiles { get; init; } = [];

    public static MonitorSettings CreateDefault(string monitorId) => new() { MonitorId = monitorId };

    public MonitorSettings AddFolder(string folder) => this with { Folders = FolderList.Add(Folders, folder) };

    public MonitorSettings RemoveFolder(string folder) => this with { Folders = FolderList.Remove(Folders, folder) };

    /// <summary>指定したマスの設定を取得する。未設定の場合は既定値。</summary>
    public TileSettings GetTile(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return index < Tiles.Count ? Tiles[index] : new TileSettings();
    }

    /// <summary>指定したマスの設定を置き換えた新しい設定を返す。</summary>
    public MonitorSettings WithTile(int index, TileSettings tile)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentNullException.ThrowIfNull(tile);

        var tiles = new List<TileSettings>(Tiles);
        while (tiles.Count <= index)
        {
            tiles.Add(new TileSettings());
        }

        tiles[index] = tile;
        return this with { Tiles = tiles };
    }

    public MonitorSettings AddTileFolder(int index, string folder) => WithTile(index, GetTile(index).AddFolder(folder));

    public MonitorSettings RemoveTileFolder(int index, string folder) => WithTile(index, GetTile(index).RemoveFolder(folder));

    /// <summary>
    /// 分割表示で、いずれかの区画に専用フォルダが設定されているか。
    /// true の場合、専用フォルダの無い区画のマスは黒で表示する。
    /// </summary>
    public bool UsesTileFolders =>
        Layout != WallpaperLayout.SingleImage && AnyQuadrantHasFolders;

    /// <summary>
    /// マスで実際に使うフォルダ。空のリストはそのマスに画像を置かない（黒で表示する）ことを表す。
    /// どの区画にも専用フォルダが無ければ全マスで 1 枚表示のフォルダを使い、
    /// 1 つでもあれば各マスが属する区画の専用フォルダだけを使う。
    /// </summary>
    /// <param name="index">現在のレイアウトでのマスの番号（左上から右方向、次の行へ、の順）。</param>
    public IReadOnlyList<string> GetEffectiveTileFolders(int index) =>
        UsesTileFolders ? GetTile(Layout.GetQuadrant(index)).Folders : Folders;

    /// <summary>現在のレイアウトで画像の取得元となるフォルダが 1 つでも設定されているか。</summary>
    public bool HasAnyFolder() =>
        Folders.Count > 0
        || UsesTileFolders;

    private bool AnyQuadrantHasFolders =>
        Enumerable.Range(0, WallpaperLayoutExtensions.QuadrantCount).Any(i => GetTile(i).Folders.Count > 0);

    /// <summary>読み込んだ設定の null を補正する。</summary>
    internal MonitorSettings Normalize() => this with
    {
        Folders = Folders ?? [],
        Tiles = [.. (Tiles ?? []).Select(t => (t ?? new TileSettings()) with { Folders = t?.Folders ?? [] })],
    };
}
