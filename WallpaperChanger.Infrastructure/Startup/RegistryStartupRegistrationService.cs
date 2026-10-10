using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Win32;
using WallpaperChanger.Core.Interfaces;

namespace WallpaperChanger.Infrastructure.Startup;

/// <summary>
/// HKCU の Run キーで自動起動を登録する（現在のユーザーのみ、管理者権限は不要）。
/// 一時フォルダ（zip ファイルから直接実行した場合の展開先など）の実行ファイルは、後で消えて自動起動が壊れるため登録しない。
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
        if (IsInTemporaryDirectory(ExecutablePath))
        {
            throw new InvalidOperationException(
                "一時フォルダから実行しているため、自動起動を登録できません。zip ファイルから直接実行している場合は、任意のフォルダに展開してから設定してください。");
        }

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

        // 登録先の実行ファイルが残っている場合は、別の場所のコピー（開発用のビルドなど）を起動しただけとみなし、登録を変えない
        if (ParseExecutablePath(registered) is { } registeredPath && File.Exists(registeredPath))
        {
            LogRegisteredPathKept(registered, Command);
            return false;
        }

        if (IsInTemporaryDirectory(ExecutablePath))
        {
            LogTemporaryPathNotRegistered(ExecutablePath);
            return false;
        }

        run.SetValue(_options.ValueName, Command, RegistryValueKind.String);
        LogPathUpdated(registered, Command);
        return true;
    }

    /// <summary>Run キーに登録されたコマンドラインから実行ファイルのパスを取り出す。取り出せない場合は null。</summary>
    internal static string? ParseExecutablePath(string command)
    {
        string trimmed = Environment.ExpandEnvironmentVariables(command).Trim();
        if (trimmed.StartsWith('"'))
        {
            int end = trimmed.IndexOf('"', 1);
            return end > 1 ? trimmed[1..end] : null;
        }

        // 引用符の無いパスは空白で引数と区切れないため、全体を 1 つのパスとして扱う
        return trimmed.Length > 0 ? trimmed : null;
    }

    private bool IsInTemporaryDirectory(string path)
    {
        string temp = Path.TrimEndingDirectorySeparator(ToLongPath(_options.TemporaryDirectory)) + Path.DirectorySeparatorChar;
        return ToLongPath(path).StartsWith(temp, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 8.3 形式（例: C:\Users\ABCDEF~1\AppData\Local\Temp）と長い形式のどちらで渡されても比較できるよう、長い形式に揃える。
    /// 存在しないパスなどで変換できない場合は完全パスのまま返す。
    /// </summary>
    private static string ToLongPath(string path)
    {
        const uint BufferLength = 32767; // 拡張パスの最大長

        string fullPath = Path.GetFullPath(path);
        char[] buffer = new char[BufferLength];
        uint length = GetLongPathName(fullPath, buffer, BufferLength);
        return length is > 0 and < BufferLength ? new string(buffer, 0, (int)length) : fullPath;
    }

    [DllImport("kernel32.dll", EntryPoint = "GetLongPathNameW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint GetLongPathName(string shortPath, [Out] char[] longPath, uint bufferLength);

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

    [LoggerMessage(Level = LogLevel.Information, Message = "登録済みの実行ファイルが存在するため、自動起動の登録パスは変更しません: {RegisteredCommand}（現在: {CurrentCommand}）")]
    private partial void LogRegisteredPathKept(string registeredCommand, string currentCommand);

    [LoggerMessage(Level = LogLevel.Warning, Message = "一時フォルダから実行しているため、自動起動の登録パスを更新しません: {ExecutablePath}")]
    private partial void LogTemporaryPathNotRegistered(string executablePath);
}
