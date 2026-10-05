using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WallpaperChanger.UI.ViewModels;

/// <summary>
/// 4 分割表示の 1 マス分。
/// </summary>
public sealed partial class TileItemViewModel : ObservableObject
{
    private static readonly string[] PositionNames = ["左上", "右上", "左下", "右下"];

    public TileItemViewModel(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        Index = index;
        Position = index < PositionNames.Length ? PositionNames[index] : $"マス {index + 1}";
    }

    /// <summary>左上から右方向、次の行へ、の順の番号（0 始まり）。</summary>
    public int Index { get; }

    public string Position { get; }

    /// <summary>このマス専用のフォルダ。</summary>
    public ObservableCollection<string> Folders { get; } = [];

    public bool HasOwnFolders => Folders.Count > 0;

    /// <summary>未設定のときに黒で表示されるか（他のマスに専用フォルダがある場合）。</summary>
    public bool IsBlank { get; private set; }

    /// <summary>マスに表示する概要（例: 「Photos ほか 1 件」「未設定（黒）」）。</summary>
    public string Summary => Folders.Count switch
    {
        0 when IsBlank => "未設定（黒）",
        0 => "未設定",
        1 => GetFolderName(Folders[0]),
        _ => $"{GetFolderName(Folders[0])} ほか {Folders.Count - 1} 件",
    };

    /// <param name="folders">このマス専用のフォルダ。</param>
    /// <param name="isBlankWhenEmpty">専用フォルダが無い場合に黒で表示されるか。</param>
    public void SetFolders(IEnumerable<string> folders, bool isBlankWhenEmpty)
    {
        Folders.Clear();
        foreach (string folder in folders)
        {
            Folders.Add(folder);
        }

        IsBlank = Folders.Count == 0 && isBlankWhenEmpty;
        OnPropertyChanged(nameof(HasOwnFolders));
        OnPropertyChanged(nameof(IsBlank));
        OnPropertyChanged(nameof(Summary));
    }

    private static string GetFolderName(string folder)
    {
        string name = Path.GetFileName(folder);
        return string.IsNullOrEmpty(name) ? folder : name;
    }
}
