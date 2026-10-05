using WallpaperChanger.Core.Models;

namespace WallpaperChanger.UI.ViewModels;

/// <summary>
/// モニター一覧の 1 行分。
/// </summary>
public sealed class MonitorItemViewModel(MonitorInfo monitor, int number)
{
    public MonitorInfo Monitor { get; } = monitor ?? throw new ArgumentNullException(nameof(monitor));

    public string Id => Monitor.Id;

    /// <summary>画面配置順（左から 1, 2, ...）の番号。</summary>
    public int Number { get; } = number;

    public string DisplayName => Monitor.DisplayName;

    public string ResolutionText => Monitor.ResolutionText;

    public bool IsPrimary => Monitor.IsPrimary;

    public string Details => IsPrimary ? $"{ResolutionText} ・ メイン" : ResolutionText;
}
