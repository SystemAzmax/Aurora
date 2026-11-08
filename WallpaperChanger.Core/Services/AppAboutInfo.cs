using System.Globalization;

namespace WallpaperChanger.Core.Services;

/// <summary>
/// 「このアプリについて」に表示する文を作成するクラスです。
/// </summary>
/// <remarks>
/// StoreReleaseKit のひな形をもとにしている。このアプリは英語でも表示するため、
/// 版の種類などの文言を <see cref="Texts"/> で差し替えられるようにした（既定は日本語）。
/// </remarks>
public static class AppAboutInfo
{
    /// <summary>
    /// バージョンを表示用の文字列にします。
    /// ストアに出すパッケージは 4 つ目の数字（リビジョン）が 0 のため、0 の場合は省きます。
    /// </summary>
    /// <param name="version">表示するバージョン。</param>
    /// <returns>「1.0.0」や「1.0.0.1」の形のバージョン。</returns>
    public static string FormatVersion(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);

        var build = Math.Max(version.Build, 0);
        return version.Revision > 0
            ? $"{version.Major}.{version.Minor}.{build}.{version.Revision}"
            : $"{version.Major}.{version.Minor}.{build}";
    }

    /// <summary>
    /// 「このアプリについて」の本文を、日本語で作成します。
    /// </summary>
    /// <param name="productName">アプリ名。</param>
    /// <param name="version">アプリのバージョン。</param>
    /// <param name="isPackaged">ストア版（パッケージあり）の場合は <c>true</c>、exe 版の場合は <c>false</c>。</param>
    /// <param name="copyright">著作権表記。無い場合は null。</param>
    /// <returns>アプリ名・バージョン・著作権表記を含む本文。</returns>
    public static string CreateMessage(
        string productName,
        Version version,
        bool isPackaged,
        string? copyright) =>
        CreateMessage(productName, version, isPackaged, copyright, Texts.Japanese);

    /// <summary>
    /// 「このアプリについて」の本文を、指定の文言で作成します。
    /// </summary>
    /// <param name="productName">アプリ名。</param>
    /// <param name="version">アプリのバージョン。</param>
    /// <param name="isPackaged">ストア版（パッケージあり）の場合は <c>true</c>、exe 版の場合は <c>false</c>。</param>
    /// <param name="copyright">著作権表記。無い場合は null。</param>
    /// <param name="texts">バージョンの行と版の種類の文言。</param>
    /// <returns>アプリ名・バージョン・著作権表記を含む本文。</returns>
    public static string CreateMessage(
        string productName,
        Version version,
        bool isPackaged,
        string? copyright,
        Texts texts)
    {
        ArgumentNullException.ThrowIfNull(texts);

        var edition = isPackaged ? texts.StoreEdition : texts.ExeEdition;
        var versionLine = string.Format(CultureInfo.InvariantCulture, texts.VersionLineFormat, FormatVersion(version), edition);
        var message = $"{productName}\n{versionLine}";
        return string.IsNullOrWhiteSpace(copyright)
            ? message
            : $"{message}\n\n{copyright}";
    }

    /// <summary>
    /// 本文のうち、言語によって変わる文言。
    /// </summary>
    /// <param name="VersionLineFormat">バージョンの行の書式。{0} にバージョン、{1} に版の種類が入る。</param>
    /// <param name="StoreEdition">ストア版（パッケージあり）の呼び方。</param>
    /// <param name="ExeEdition">exe 版（パッケージなし）の呼び方。</param>
    public sealed record Texts(string VersionLineFormat, string StoreEdition, string ExeEdition)
    {
        /// <summary>日本語の文言（StoreReleaseKit の決まりの表示）。</summary>
        public static Texts Japanese { get; } = new("バージョン {0}（{1}）", "ストア版", "exe 版");
    }
}
