namespace WallpaperChanger.Core.Models;

/// <summary>
/// 画像を表示する枠（画面またはマス）への収め方。Windows の「調整方法」に当たる。
/// </summary>
public enum ImageFit
{
    /// <summary>枠全体を覆うように拡大縮小し、はみ出した部分は中央で切り抜く（Windows の「ページ幅に合わせる」）。</summary>
    Fill = 0,

    /// <summary>画像全体が枠に収まるように拡大縮小し、余白を残す（Windows の「画面のサイズに合わせる」）。</summary>
    Fit = 1,

    /// <summary>縦横比を変えて枠いっぱいに引き伸ばす（Windows の「拡大して表示」）。</summary>
    Stretch = 2,

    /// <summary>拡大縮小せずに中央に置く。枠より大きい画像は中央を切り抜く（Windows の「中央に表示」）。</summary>
    Center = 3,
}
