using WallpaperChanger.Core.Models;
using WallpaperChanger.Tests;
using WallpaperChanger.UI.Resources;

[assembly: AssemblyFixture(typeof(DisplayLanguageFixture))]

namespace WallpaperChanger.Tests;

/// <summary>
/// テストは日本語の文言で確かめるため、Windows の表示言語にかかわらず、すべてのテストの前に表示言語を日本語にする
/// （CI の実行環境は英語のため、そのままでは英語の文言になる）。
/// </summary>
/// <remarks>
/// 表示言語はプロセス全体の設定のため、テストの中で英語に切り替えない（並列に動くほかのテストの文言が変わる）。
/// 英語の文言は、<see cref="Localizer.Get"/> などで言語を指定して確かめる。
/// </remarks>
public sealed class DisplayLanguageFixture
{
    public DisplayLanguageFixture() => Localizer.Apply(AppLanguage.Japanese);
}
