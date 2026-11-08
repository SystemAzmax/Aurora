using System.IO;
using System.Reflection;
using WallpaperChanger.Core.Services;
using WallpaperChanger.UI.Resources;

namespace WallpaperChanger.UI.Services
{
    /// <summary>
    /// 実行中のアプリのバージョンなどの情報を取得するクラス。
    /// </summary>
    /// <remarks>
    /// StoreReleaseKit のひな形（TargetFramework に Windows のバージョンが付いている WPF 用）をもとにしている。
    /// 「このアプリについて」の本文は、表示言語の文言で作る。
    /// </remarks>
    public static class AppInfoProvider
    {
        /// <summary>アプリ名を取得できない場合に使う名前。</summary>
        private const string DEFAULT_PRODUCT_NAME = "Aurora Wallpaper Changer";

        /// <summary>使用しているソフトウェアのライセンス表示のファイル名。</summary>
        private const string THIRD_PARTY_NOTICES_FILE_NAME = "ThirdPartyNotices.txt";

        /// <summary>
        /// 使用しているソフトウェアのライセンス表示のファイルのパスを取得します。
        /// ファイルはアプリと同じフォルダーに配置されます。
        /// </summary>
        public static string ThirdPartyNoticesPath =>
            Path.Combine(AppContext.BaseDirectory, THIRD_PARTY_NOTICES_FILE_NAME);

        /// <summary>
        /// アプリ名を取得します。
        /// </summary>
        /// <returns>アプリ名。</returns>
        public static string GetProductName() =>
            typeof(AppInfoProvider).Assembly
                .GetCustomAttribute<AssemblyProductAttribute>()?.Product
            ?? DEFAULT_PRODUCT_NAME;

        /// <summary>
        /// アプリのバージョンと、ストア版（パッケージあり）かどうかを取得します。
        /// ストア版は Package.appxmanifest のバージョン、
        /// exe 版はアプリの csproj の Version から作られるアセンブリのバージョンを返します。
        /// </summary>
        /// <returns>バージョンと、ストア版かどうか。</returns>
        public static (Version Version, bool IsPackaged) GetVersion()
        {
            try
            {
                var version = Windows.ApplicationModel.Package.Current.Id.Version;
                return (
                    new Version(version.Major, version.Minor, version.Build, version.Revision),
                    true);
            }
            catch (Exception)
            {
                // exe 版（パッケージなし）では Package.Current を使えないため、
                // アセンブリのバージョンを使う
                var assemblyVersion = typeof(AppInfoProvider).Assembly.GetName().Version
                    ?? new Version(0, 0, 0, 0);
                return (assemblyVersion, false);
            }
        }

        /// <summary>
        /// 「このアプリについて」の本文を、表示言語で作成します。
        /// </summary>
        /// <returns>アプリ名・バージョン・著作権表記を含む本文。</returns>
        public static string CreateAboutMessage()
        {
            var (version, isPackaged) = GetVersion();
            var copyright = typeof(AppInfoProvider).Assembly
                .GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright;

            return AppAboutInfo.CreateMessage(
                GetProductName(),
                version,
                isPackaged,
                copyright,
                new AppAboutInfo.Texts(Strings.AboutVersionLineFormat, Strings.AboutStoreEdition, Strings.AboutExeEdition));
        }
    }
}
