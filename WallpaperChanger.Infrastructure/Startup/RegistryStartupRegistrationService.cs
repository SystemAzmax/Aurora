using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Win32;
using WallpaperChanger.Core.Interfaces;

namespace WallpaperChanger.Infrastructure.Startup;

/// <summary>
/// HKCU の Run キーで自動起動を登録する（現在のユーザーのみ、管理者権限は不要）。
/// </summary>
internal sealed partial class RegistryStartupRegistrationService : IStartupRegistrationService
{
    private readonly StartupRegistrationOptions _options;
    private readonly ILogger<RegistryStartupRegistrationService> _logger;

    public RegistryStartupRegistrationService(
        IOptions<StartupRegistrationOptions> options,
        ILogger<RegistryStartupRegistrationService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Run キーに登録するコマンドライン（パスに空白があっても動くよう引用符で囲む）。</summary>
    internal string Command => $"\"{ExecutablePath}\"";

    private string ExecutablePath =>
        _options.ExecutablePath
        ?? Environment.ProcessPath
        ?? throw new InvalidOperationException("実行ファイルのパスを取得できません。");

    public bool IsEnabled()
    {
        using RegistryKey? run = Registry.CurrentUser.OpenSubKey(_options.RunKeyPath);
        return run?.GetValue(_options.ValueName) is string && !IsDisabledInTaskManager();
    }

    public void Enable()
    {
        using (RegistryKey run = Registry.CurrentUser.CreateSubKey(_options.RunKeyPath, writable: true))
        {
            run.SetValue(_options.ValueName, Command, RegistryValueKind.String);
        }

        // タスク マネージャーで無効にされていると Run に登録しても起動されないため、その記録を消す
        DeleteStartupApprovedValue();
        LogEnabled(Command);
    }

    public void Disable()
    {
        using (RegistryKey? run = Registry.CurrentUser.OpenSubKey(_options.RunKeyPath, writable: true))
        {
            run?.DeleteValue(_options.ValueName, throwOnMissingValue: false);
        }

        DeleteStartupApprovedValue();
        LogDisabled();
    }

    public bool UpdateRegisteredPathIfNeeded()
    {
        using RegistryKey? run = Registry.CurrentUser.OpenSubKey(_options.RunKeyPath, writable: true);
        if (run?.GetValue(_options.ValueName) is not string registered
            || string.Equals(registered, Command, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        run.SetValue(_options.ValueName, Command, RegistryValueKind.String);
        LogPathUpdated(registered, Command);
        return true;
    }

    /// <summary>
    /// StartupApproved の値は 12 バイトのバイナリで、先頭バイトの最下位ビットが 1 のとき無効を表す。
    /// 値が無い場合は有効として扱われる。
    /// </summary>
    private bool IsDisabledInTaskManager()
    {
        using RegistryKey? approved = Registry.CurrentUser.OpenSubKey(_options.StartupApprovedKeyPath);
        return approved?.GetValue(_options.ValueName) is byte[] { Length: > 0 } data && (data[0] & 0x01) == 0x01;
    }

    private void DeleteStartupApprovedValue()
    {
        using RegistryKey? approved = Registry.CurrentUser.OpenSubKey(_options.StartupApprovedKeyPath, writable: true);
        approved?.DeleteValue(_options.ValueName, throwOnMissingValue: false);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Windows 起動時の自動起動を有効にしました: {Command}")]
    private partial void LogEnabled(string command);

    [LoggerMessage(Level = LogLevel.Information, Message = "Windows 起動時の自動起動を無効にしました")]
    private partial void LogDisabled();

    [LoggerMessage(Level = LogLevel.Information, Message = "自動起動の登録パスを更新しました: {OldCommand} -> {NewCommand}")]
    private partial void LogPathUpdated(string oldCommand, string newCommand);
}
