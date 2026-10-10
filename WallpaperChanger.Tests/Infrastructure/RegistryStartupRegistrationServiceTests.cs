using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Win32;
using WallpaperChanger.Infrastructure.Startup;
using WallpaperChanger.Tests.Fakes;

namespace WallpaperChanger.Tests.Infrastructure;

/// <summary>
/// 本物の Run キーには触れず、HKCU 配下の一時キーで検証する。
/// </summary>
public sealed class RegistryStartupRegistrationServiceTests : IDisposable
{
    private const string ValueName = "WallpaperChanger";
    private const string ExecutablePath = @"C:\Program Files\Wallpaper Changer\WallpaperChanger.exe";

    private const string TestRootPath = @"Software\WallpaperChanger.Tests";

    /// <summary>テストで一時フォルダとして扱う場所（実在しなくてよい）。</summary>
    private const string FakeTemporaryDirectory = @"C:\FakeTemp";

    private readonly string _rootPath = $@"{TestRootPath}\{Guid.NewGuid():N}";
    private readonly TempDirectory _temp = new();
    private readonly RegistryStartupRegistrationService _sut;

    public RegistryStartupRegistrationServiceTests()
    {
        _sut = CreateSut(ExecutablePath);
    }

    private string RunKeyPath => $@"{_rootPath}\Run";

    private string ApprovedKeyPath => $@"{_rootPath}\StartupApproved\Run";

    public void Dispose()
    {
        _temp.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_rootPath, throwOnMissingSubKey: false);

        // 他のテストが使用中でなければ、共通の親キーも残さない
        using RegistryKey? parent = Registry.CurrentUser.OpenSubKey(TestRootPath);
        if (parent is { SubKeyCount: 0, ValueCount: 0 })
        {
            Registry.CurrentUser.DeleteSubKey(TestRootPath, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void 有効にすると引用符付きの実行ファイルパスを登録する()
    {
        _sut.Enable();

        Assert.Equal($"\"{ExecutablePath}\"", ReadRunValue());
        Assert.True(_sut.IsEnabled());
    }

    [Fact]
    public void 無効にすると登録を削除する()
    {
        _sut.Enable();

        _sut.Disable();

        Assert.Null(ReadRunValue());
        Assert.False(_sut.IsEnabled());
    }

    [Fact]
    public void 未登録なら無効で無効化しても例外にならない()
    {
        Assert.False(_sut.IsEnabled());

        _sut.Disable();

        Assert.False(_sut.IsEnabled());
    }

    [Theory]
    [InlineData((byte)0x03, false)] // タスク マネージャーで無効
    [InlineData((byte)0x02, true)]  // タスク マネージャーで有効
    public void タスクマネージャーでの有効無効を反映する(byte flag, bool expected)
    {
        _sut.Enable();
        WriteApprovedValue(flag);

        Assert.Equal(expected, _sut.IsEnabled());
    }

    [Fact]
    public void タスクマネージャーで無効にされていても有効に戻せる()
    {
        _sut.Enable();
        WriteApprovedValue(0x03);
        Assert.False(_sut.IsEnabled());

        _sut.Enable();

        Assert.True(_sut.IsEnabled());
        using RegistryKey? approved = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath);
        Assert.Null(approved?.GetValue(ValueName));
    }

    [Fact]
    public void 登録パスが古ければ現在の実行ファイルに更新する()
    {
        CreateSut(@"C:\Old\WallpaperChanger.exe").Enable();

        bool updated = _sut.UpdateRegisteredPathIfNeeded();

        Assert.True(updated);
        Assert.Equal($"\"{ExecutablePath}\"", ReadRunValue());
    }

    [Fact]
    public void 登録パスが同じか未登録なら更新しない()
    {
        Assert.False(_sut.UpdateRegisteredPathIfNeeded());
        Assert.Null(ReadRunValue());

        _sut.Enable();

        Assert.False(_sut.UpdateRegisteredPathIfNeeded());
    }

    [Fact]
    public void 登録先の実行ファイルが存在すれば別の場所から起動しても登録を変えない()
    {
        string installed = _temp.CreateFile(@"Installed\WallpaperChanger.exe");
        WriteRunValue($"\"{installed}\"");

        bool updated = _sut.UpdateRegisteredPathIfNeeded();

        Assert.False(updated);
        Assert.Equal($"\"{installed}\"", ReadRunValue());
    }

    [Fact]
    public void 一時フォルダから起動した場合は登録先が無くても登録を変えない()
    {
        CreateSut(@"C:\Old\WallpaperChanger.exe").Enable();
        RegistryStartupRegistrationService sut = CreateSut($@"{FakeTemporaryDirectory}\Temp1_app.zip\WallpaperChanger.exe");

        bool updated = sut.UpdateRegisteredPathIfNeeded();

        Assert.False(updated);
        Assert.Equal("\"C:\\Old\\WallpaperChanger.exe\"", ReadRunValue());
    }

    [Fact]
    public void 一時フォルダから起動した場合は有効にできない()
    {
        RegistryStartupRegistrationService sut = CreateSut($@"{FakeTemporaryDirectory}\Temp1_app.zip\WallpaperChanger.exe");

        var ex = Assert.Throws<InvalidOperationException>(sut.Enable);

        Assert.Contains("一時フォルダ", ex.Message, StringComparison.Ordinal);
        Assert.Null(ReadRunValue());
    }

    [Fact]
    public void 一時フォルダと名前が前方一致するだけのフォルダからは有効にできる()
    {
        string executable = $@"{FakeTemporaryDirectory}Other\WallpaperChanger.exe";

        CreateSut(executable).Enable();

        Assert.Equal($"\"{executable}\"", ReadRunValue());
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\App\\app.exe\"", @"C:\Program Files\App\app.exe")]
    [InlineData("\"C:\\Program Files\\App\\app.exe\" --minimized", @"C:\Program Files\App\app.exe")]
    [InlineData(@"  C:\App\app.exe  ", @"C:\App\app.exe")]
    [InlineData("\"", null)]
    [InlineData("\"\"", null)]
    [InlineData("   ", null)]
    public void 登録値から実行ファイルのパスを取り出す(string command, string? expected)
    {
        Assert.Equal(expected, RegistryStartupRegistrationService.ParseExecutablePath(command));
    }

    [Fact]
    public void 登録値の環境変数は展開して実行ファイルを探す()
    {
        string expected = Path.Combine(Environment.GetEnvironmentVariable("SystemRoot")!, "notepad.exe");

        Assert.Equal(expected, RegistryStartupRegistrationService.ParseExecutablePath("\"%SystemRoot%\\notepad.exe\""));
    }

    private RegistryStartupRegistrationService CreateSut(string executablePath) => new(
        Options.Create(new StartupRegistrationOptions
        {
            RunKeyPath = RunKeyPath,
            StartupApprovedKeyPath = ApprovedKeyPath,
            ValueName = ValueName,
            ExecutablePath = executablePath,
            TemporaryDirectory = FakeTemporaryDirectory,
        }),
        NullLogger<RegistryStartupRegistrationService>.Instance);

    private string? ReadRunValue()
    {
        using RegistryKey? run = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return run?.GetValue(ValueName) as string;
    }

    private void WriteRunValue(string command)
    {
        using RegistryKey run = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        run.SetValue(ValueName, command, RegistryValueKind.String);
    }

    private void WriteApprovedValue(byte flag)
    {
        using RegistryKey approved = Registry.CurrentUser.CreateSubKey(ApprovedKeyPath);
        approved.SetValue(ValueName, new byte[] { flag, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
    }
}
