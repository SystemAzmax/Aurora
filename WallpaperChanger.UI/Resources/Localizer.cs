using System.Globalization;
using WallpaperChanger.Core.Models;
using WallpaperChanger.Infrastructure;

namespace WallpaperChanger.UI.Resources;

/// <summary>
/// 表示言語の切り替えと、文言の書式設定。
/// 文言は Strings.resx（日本語、既定）と Strings.en.resx（英語）に置き、生成される <see cref="Strings"/> から取り出す。
/// </summary>
/// <remarks>
/// 言語はアプリの起動時（設定の読み込み直後、画面を作る前）に 1 回だけ反映する。
/// 画面の文言は作るときに取り出すため、設定で言語を変えた場合は、アプリを再起動すると反映される。
/// ログは調査用のため、言語にかかわらず日本語のまま書く。
/// </remarks>
public static class Localizer
{
    /// <summary>今の表示言語（起動時に反映した言語）。</summary>
    public static AppLanguage CurrentLanguage { get; private set; } = AppLanguage.Japanese;

    /// <summary>表示言語に当たるカルチャ。</summary>
    public static CultureInfo GetCulture(AppLanguage language) => language switch
    {
        AppLanguage.English => CultureInfo.GetCultureInfo("en-US"),
        _ => CultureInfo.GetCultureInfo("ja-JP"),
    };

    /// <summary>
    /// 表示言語を反映する。どのスレッドから取り出しても同じ言語になるよう、リソースのカルチャを明示する
    /// （スレッドのカルチャに任せると、既に動いているスレッドでは Windows の表示言語になるため）。
    /// </summary>
    public static void Apply(AppLanguage language)
    {
        CultureInfo culture = GetCulture(language);
        CurrentLanguage = language;
        Strings.Culture = culture;
        InfrastructureLocalization.SetCulture(culture);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    /// <summary>リソースの書式（{0} などを含む文言）に値を埋め込む。</summary>
    public static string Format(string format, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, format, args);

    /// <summary>指定の言語の文言を取り出す（言語を変えたときに、変えた先の言語で案内するため）。</summary>
    public static string Get(string name, AppLanguage language) =>
        Strings.ResourceManager.GetString(name, GetCulture(language)) ?? name;
}
