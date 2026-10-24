namespace WallpaperChanger.UI.ViewModels;

/// <summary>
/// 「ほかのモニターの設定をコピー」のコピー元の候補（接続していないモニターを含む）。
/// </summary>
/// <param name="MonitorId">コピー元のモニターの ID。</param>
/// <param name="Name">モニターの呼び方（接続中なら「番号. 名前」）。</param>
/// <param name="IsConnected">接続中か。</param>
public sealed record MonitorCopySourceViewModel(string MonitorId, string Name, bool IsConnected)
{
    /// <summary>一覧に表示する文言。</summary>
    public string Label => IsConnected ? Name : $"{Name}（未接続）";
}
