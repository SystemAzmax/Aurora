namespace WallpaperChanger.Core.Models;

/// <summary>
/// 複数モニターにまたがる 1 枚表示（スパン）の設定。表示レイアウトにスパンを選んだモニターで共通に使う。
/// 不変オブジェクトとして扱い、変更は with 式で行う。
/// </summary>
public sealed record SpanSettings
{
    /// <summary>スパンで表示する画像のフォルダ一覧。</summary>
    public IReadOnlyList<string> Folders { get; init; } = [];

    /// <summary>サブフォルダも検索するか。</summary>
    public bool IncludeSubfolders { get; init; } = true;

    /// <summary>次の壁紙の選び方。</summary>
    public ImageSelectionMode SelectionMode { get; init; } = ImageSelectionMode.Random;

    /// <summary>
    /// 画像を、スパンに参加するモニター全体を囲む範囲に収める方法（<see cref="ImageFit.Fill"/> か <see cref="ImageFit.Fit"/>）。
    /// </summary>
    public ImageFit Fit { get; init; } = ImageFit.Fill;

    public SpanSettings AddFolder(string folder) => this with { Folders = PathList.Add(Folders, folder) };

    public SpanSettings RemoveFolder(string folder) => this with { Folders = PathList.Remove(Folders, folder) };

    /// <summary>読み込んだ設定の null と範囲外の値を補正する。</summary>
    internal SpanSettings Normalize() => this with
    {
        Folders = Folders ?? [],
        SelectionMode = Enum.IsDefined(SelectionMode) ? SelectionMode : ImageSelectionMode.Random,
        Fit = Fit is ImageFit.Fill or ImageFit.Fit ? Fit : ImageFit.Fill,
    };
}
