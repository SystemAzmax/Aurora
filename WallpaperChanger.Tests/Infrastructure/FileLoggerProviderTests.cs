using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using WallpaperChanger.Infrastructure.Logging;
using WallpaperChanger.Tests.Fakes;

namespace WallpaperChanger.Tests.Infrastructure;

public sealed class FileLoggerProviderTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    // FakeTimeProvider は開始時刻を UTC として扱うため UTC で指定する（日本時間 2026-10-05 09:30:15）
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 5, 0, 30, 15, TimeSpan.Zero));

    public FileLoggerProviderTests()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("JST", TimeSpan.FromHours(9), "JST", "JST"));
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void 日付ごとのファイルに時刻レベルカテゴリ付きで書き込む()
    {
        using (FileLoggerProvider provider = CreateSut())
        {
            provider.CreateLogger("WallpaperChanger.Test").LogInformation("壁紙を変更しました: {Path}", @"C:\w\1.jpg");
        }

        string log = File.ReadAllText(Path.Combine(_temp.Path, "wallpaperchanger-20261005.log"));
        Assert.Equal($"2026-10-05 09:30:15.000 [INF] WallpaperChanger.Test: 壁紙を変更しました: C:\\w\\1.jpg{Environment.NewLine}", log);
    }

    [Fact]
    public void 最低レベル未満のログは書き込まない()
    {
        using (FileLoggerProvider provider = CreateSut(minimumLevel: LogLevel.Information))
        {
            ILogger logger = provider.CreateLogger("Cat");
            Assert.False(logger.IsEnabled(LogLevel.Debug));
            logger.LogDebug("debug message");
            logger.LogWarning("warning message");
        }

        string log = ReadAllLogs();
        Assert.DoesNotContain("debug message", log, StringComparison.Ordinal);
        Assert.Contains("[WRN] Cat: warning message", log, StringComparison.Ordinal);
    }

    [Fact]
    public void 例外の詳細も書き込む()
    {
        using (FileLoggerProvider provider = CreateSut())
        {
            provider.CreateLogger("Cat").LogError(new InvalidOperationException("壊れた画像"), "失敗しました");
        }

        string log = ReadAllLogs();
        Assert.Contains("[ERR] Cat: 失敗しました", log, StringComparison.Ordinal);
        Assert.Contains("System.InvalidOperationException: 壊れた画像", log, StringComparison.Ordinal);
    }

    [Fact]
    public void 日付が変わると新しいファイルに切り替え保存期間を過ぎたファイルを削除する()
    {
        _temp.CreateFile("wallpaperchanger-20260901.log", "old");   // 保存期間外
        _temp.CreateFile("wallpaperchanger-20260925.log", "keep");  // 保存期間内
        _temp.CreateFile("other.log", "unrelated");

        using (FileLoggerProvider provider = CreateSut(retentionDays: 14))
        {
            ILogger logger = provider.CreateLogger("Cat");
            logger.LogInformation("day1");
            WaitForFile("wallpaperchanger-20261005.log");

            _time.Advance(TimeSpan.FromDays(1));
            logger.LogInformation("day2");
        }

        Assert.Contains("day1", File.ReadAllText(Path.Combine(_temp.Path, "wallpaperchanger-20261005.log")), StringComparison.Ordinal);
        Assert.Contains("day2", File.ReadAllText(Path.Combine(_temp.Path, "wallpaperchanger-20261006.log")), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_temp.Path, "wallpaperchanger-20260901.log")));
        Assert.True(File.Exists(Path.Combine(_temp.Path, "wallpaperchanger-20260925.log")));
        Assert.True(File.Exists(Path.Combine(_temp.Path, "other.log")));
    }

    [Fact]
    public void 実行中でもログファイルを読み取れる()
    {
        using FileLoggerProvider provider = CreateSut();
        provider.CreateLogger("Cat").LogInformation("while running");
        string path = WaitForFile("wallpaperchanger-20261005.log");

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);

        Assert.Contains("while running", reader.ReadToEnd(), StringComparison.Ordinal);
    }

    [Fact]
    public void 例外の詳細を取得できなくてもログを書き込み続ける()
    {
        using (FileLoggerProvider provider = CreateSut())
        {
            ILogger logger = provider.CreateLogger("Cat");
            logger.LogError(new BrokenException(), "失敗しました");
            logger.LogInformation("その後のログ");
        }

        string log = ReadAllLogs();
        Assert.Contains("[ERR] Cat: 失敗しました", log, StringComparison.Ordinal);
        Assert.Contains(typeof(BrokenException).FullName!, log, StringComparison.Ordinal);
        Assert.Contains("[INF] Cat: その後のログ", log, StringComparison.Ordinal);
    }

    [Fact]
    public void 古いログを整理するためのフォルダの一覧を取得できなくてもログを書き込む()
    {
        // 一覧の取得（古いログの整理）だけを拒否し、ファイルの作成・書き込みは許可する
        string directory = Path.Combine(_temp.Path, "logs");
        var info = new DirectoryInfo(directory);
        info.Create();
        var denyList = new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().User!, FileSystemRights.ListDirectory, AccessControlType.Deny);
        DirectorySecurity security = info.GetAccessControl();
        security.AddAccessRule(denyList);
        info.SetAccessControl(security);

        try
        {
            Assert.Throws<UnauthorizedAccessException>(() => Directory.GetFiles(directory));

            using FileLoggerProvider provider = CreateSut(directory: directory);
            provider.CreateLogger("Cat").LogInformation("一覧を取得できなくても書き込む");
        }
        finally
        {
            security = info.GetAccessControl();
            security.RemoveAccessRule(denyList);
            info.SetAccessControl(security);
        }

        string log = File.ReadAllText(Path.Combine(directory, "wallpaperchanger-20261005.log"));
        Assert.Contains("一覧を取得できなくても書き込む", log, StringComparison.Ordinal);
    }

    private FileLoggerProvider CreateSut(
        LogLevel minimumLevel = LogLevel.Information, int retentionDays = 14, string? directory = null) => new(
        Options.Create(new FileLoggerOptions
        {
            Directory = directory ?? _temp.Path,
            MinimumLevel = minimumLevel,
            RetentionDays = retentionDays,
        }),
        _time);

    private string ReadAllLogs() =>
        string.Concat(Directory.GetFiles(_temp.Path, "wallpaperchanger-*.log").Order().Select(File.ReadAllText));

    /// <summary>バックグラウンドの書き込みでファイルに内容が出るまで待つ。</summary>
    private string WaitForFile(string name)
    {
        string path = Path.Combine(_temp.Path, name);
        for (int i = 0; i < 200; i++)
        {
            if (File.Exists(path) && new FileInfo(path).Length > 0)
            {
                return path;
            }

            Thread.Sleep(10);
        }

        throw new TimeoutException($"ログファイルが作成されませんでした: {name}");
    }

    /// <summary>メッセージの取得（ToString）自体が失敗する例外。</summary>
    private sealed class BrokenException : Exception
    {
        public override string Message => throw new InvalidOperationException("メッセージを取得できません");
    }
}
