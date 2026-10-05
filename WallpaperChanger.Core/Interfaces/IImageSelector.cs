using WallpaperChanger.Core.Models;

namespace WallpaperChanger.Core.Interfaces;

/// <summary>
/// 候補画像の中から次に表示する画像を選ぶ戦略。
/// </summary>
public interface IImageSelector
{
    /// <summary>この戦略が対応する選択モード。</summary>
    ImageSelectionMode Mode { get; }

    /// <summary>
    /// 次に表示する画像を count 枚選ぶ。
    /// 候補が count 枚に満たない場合は同じ画像を繰り返して count 枚にする。
    /// </summary>
    /// <param name="images">候補画像（パス順に整列済み）。</param>
    /// <param name="currentImages">現在表示している画像。不明な場合は空。</param>
    /// <param name="count">必要な枚数（1 枚表示なら 1、4 分割なら 4）。</param>
    /// <returns>選んだ画像。候補が空の場合は空。</returns>
    IReadOnlyList<string> SelectNext(IReadOnlyList<string> images, IReadOnlyList<string> currentImages, int count);
}
