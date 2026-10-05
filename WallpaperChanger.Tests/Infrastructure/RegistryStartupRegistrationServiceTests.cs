using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Win32;
using WallpaperChanger.Infrastructure.Startup;

namespace WallpaperChanger.Tests.Infrastructure;

/// <summary>
/// 本物の Run キーには触れず、HKCU 配下の一時キーで検証する。
/// </summary>
public sealed class RegistryStartupRegistrationServiceTests : IDisposable
{
    private const string ValueName = "WallpaperChanger";
    private const string ExecutablePath = @"C:\Program Files\Wallpaper Changer\WallpaperChanger.exe";

    private const string TestRootPath = @"Software\WallpaperChanger.Tests";

    private readonly string _rootPath = $@"{TestRootPath}\{Guid.NewGuid():N}";
    private readonly RegistryStartupRegistrationService _sut;

    public RegistryStartupRegistrationServiceTests()
    {
        _sut = CreateSut(ExecutablePath);
    }

    private string RunKeyPath => $@"{_rootPath}\Run";

    private string ApprovedKeyPath => $@"{_rootPath}\StartupApproved\Run";

    public void Dispose()
    {
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

    private RegistryStartupRegistrationService CreateSut(string executablePath) => new(
        Options.Create(new StartupRegistrationOptions
        {
            RunKeyPath = RunKeyPath,
            StartupApprovedKeyPath = ApprovedKeyPath,
            ValueName = ValueName,
            ExecutablePath = executablePath,
        }),
        NullLogger<RegistryStartupRegistrationService>.Instance);

    private string? ReadRunValue()
    {
        using RegistryKey? run = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return run?.GetValue(ValueName) as string;
    }

    private void WriteApprovedValue(byte flag)
    {
        using RegistryKey approved = Registry.CurrentUser.CreateSubKey(ApprovedKeyPath);
        approved.SetValue(ValueName, new byte[] { flag, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
    }
}
