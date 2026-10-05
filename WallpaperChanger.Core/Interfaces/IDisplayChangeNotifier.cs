namespace WallpaperChanger.Core.Interfaces;

/// <summary>
/// OS からのディスプレイ構成変更（モニターの接続・切断、解像度・配置の変更）の通知を中継する。
/// 1 回の操作で短時間に複数回通知されることがある。
/// </summary>
public interface IDisplayChangeNotifier
{
    /// <summary>ディスプレイ構成が変わった可能性があるときに発生する（任意のスレッド）。</summary>
    event EventHandler? DisplaysChanged;
}
