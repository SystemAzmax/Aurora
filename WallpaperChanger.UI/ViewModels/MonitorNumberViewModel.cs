namespace WallpaperChanger.UI.ViewModels;

/// <summary>
/// モニターの識別用に、そのモニター上へ表示する番号ウィンドウの内容。
/// </summary>
public sealed class MonitorNumberViewModel(int number, string displayName, string resolutionText, bool isSelected)
{
    public int Number { get; } = number;

    public string DisplayName { get; } = displayName;

    public string ResolutionText { get; } = resolutionText;

    /// <summary>設定画面で選択中のモニターか（強調表示する）。</summary>
    public bool IsSelected { get; } = isSelected;
}
