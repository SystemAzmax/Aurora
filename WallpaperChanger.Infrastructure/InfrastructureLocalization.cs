using System.Globalization;
using WallpaperChanger.Infrastructure.Resources;

namespace WallpaperChanger.Infrastructure;

/// <summary>
/// Infrastructure 層で作る、画面に表示される文言（例外のメッセージやモニターの名前）の言語を切り替える。
/// 文言は Resources/Strings.resx（日本語、既定）と Strings.en.resx（英語）に置く。ログの文言は日本語のまま。
/// </summary>
public static class InfrastructureLocalization
{
    /// <summary>文言の言語を設定する。どのスレッドで作る文言にも反映される。</summary>
    public static void SetCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        Strings.Culture = culture;
    }

    /// <summary>
    /// リソースの書式（{0} などを含む文言）に値を埋め込む。
    /// 書式は言語によって変わるため、CompositeFormat にしてキャッシュしない。
    /// </summary>
    internal static string Format(string format, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, format, args);
}
