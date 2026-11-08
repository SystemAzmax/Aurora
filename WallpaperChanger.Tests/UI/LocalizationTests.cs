using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using WallpaperChanger.Core.Models;
using WallpaperChanger.UI.Resources;
using InfrastructureStrings = WallpaperChanger.Infrastructure.Resources.Strings;

namespace WallpaperChanger.Tests.UI;

public partial class LocalizationTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");

    public static TheoryData<string> ResourceNames => ["UI", "Infrastructure"];

    [Theory]
    [MemberData(nameof(ResourceNames))]
    public void すべての文言に英語があり埋め込む値の番号がそろっている(string name)
    {
        ResourceManager manager = GetResourceManager(name);
        Dictionary<string, string> japanese = ReadAll(manager, CultureInfo.InvariantCulture);
        Dictionary<string, string> english = ReadAll(manager, English);

        Assert.NotEmpty(japanese);
        Assert.Equal(japanese.Keys.Order(), english.Keys.Order());
        foreach ((string key, string ja) in japanese)
        {
            string en = english[key];
            if (ja.Length > 0)
            {
                Assert.False(string.IsNullOrWhiteSpace(en), $"{key} の英語がありません");
            }

            Assert.True(
                Placeholders(ja).SetEquals(Placeholders(en)),
                $"{key} の埋め込む値の番号が日本語と英語で違います: 「{ja}」「{en}」");
        }
    }

    [Theory]
    [MemberData(nameof(ResourceNames))]
    public void 既定の文言は日本語(string name)
    {
        ResourceManager manager = GetResourceManager(name);

        // 設定ファイルに言語が無い場合や、日本語を選んだ場合に使う文言
        Assert.Equal(
            ReadAll(manager, CultureInfo.InvariantCulture),
            ReadAll(manager, CultureInfo.GetCultureInfo("ja-JP"), tryParents: true));
    }

    [Fact]
    public void 表示言語に当たるカルチャ()
    {
        Assert.Equal("ja-JP", Localizer.GetCulture(AppLanguage.Japanese).Name);
        Assert.Equal("en-US", Localizer.GetCulture(AppLanguage.English).Name);
    }

    [Fact]
    public void 言語を指定して文言を取り出せる()
    {
        Assert.Equal("一時停止", Localizer.Get(nameof(Strings.Pause), AppLanguage.Japanese));
        Assert.Equal("Pause", Localizer.Get(nameof(Strings.Pause), AppLanguage.English));
    }

    private static ResourceManager GetResourceManager(string name) =>
        name == "UI" ? Strings.ResourceManager : InfrastructureStrings.ResourceManager;

    private static Dictionary<string, string> ReadAll(ResourceManager manager, CultureInfo culture, bool tryParents = false)
    {
        ResourceSet set = manager.GetResourceSet(culture, createIfNotExists: true, tryParents)
            ?? throw new InvalidOperationException($"{culture.Name} の文言がありません");
        return set.Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!);
    }

    private static HashSet<string> Placeholders(string text) =>
        [.. PlaceholderPattern().Matches(text).Select(m => m.Groups[1].Value)];

    [GeneratedRegex(@"\{(\d+)(?:[,:][^}]*)?\}")]
    private static partial Regex PlaceholderPattern();
}
