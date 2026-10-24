using System.Windows.Input;

namespace WallpaperChanger.UI.ViewModels;

/// <summary>
/// トレイのメニューの「この画像を表示しない」「画像の場所を開く」の下に並べる、表示中の画像の項目。
/// 画像の項目（<see cref="ImagePath"/> あり）か、分割表示のモニターの項目（<see cref="Children"/> にマスの画像）のどちらか。
/// </summary>
public sealed class ShownImageItemViewModel
{
    private ShownImageItemViewModel(
        string header,
        string? imagePath,
        IReadOnlyList<ShownImageItemViewModel> children,
        ICommand? excludeCommand,
        ICommand? showInFolderCommand)
    {
        Header = header;
        ImagePath = imagePath;
        Children = children;
        ExcludeCommand = excludeCommand;
        ShowInFolderCommand = showInFolderCommand;
    }

    public string Header { get; }

    /// <summary>画像のパス。モニターの項目では null。</summary>
    public string? ImagePath { get; }

    /// <summary>分割表示のモニターの項目では、マスごとの画像の項目。画像の項目では空。</summary>
    public IReadOnlyList<ShownImageItemViewModel> Children { get; }

    /// <summary>「この画像を表示しない」で実行するコマンド（引数は <see cref="ImagePath"/>）。モニターの項目では null。</summary>
    public ICommand? ExcludeCommand { get; }

    /// <summary>「画像の場所を開く」で実行するコマンド（引数は <see cref="ImagePath"/>）。モニターの項目では null。</summary>
    public ICommand? ShowInFolderCommand { get; }

    public static ShownImageItemViewModel ForImage(string header, string imagePath, ICommand excludeCommand, ICommand showInFolderCommand)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(header);
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        ArgumentNullException.ThrowIfNull(excludeCommand);
        ArgumentNullException.ThrowIfNull(showInFolderCommand);
        return new(header, imagePath, [], excludeCommand, showInFolderCommand);
    }

    public static ShownImageItemViewModel ForGroup(string header, IReadOnlyList<ShownImageItemViewModel> children)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(header);
        ArgumentNullException.ThrowIfNull(children);
        return new(header, imagePath: null, children, excludeCommand: null, showInFolderCommand: null);
    }
}
