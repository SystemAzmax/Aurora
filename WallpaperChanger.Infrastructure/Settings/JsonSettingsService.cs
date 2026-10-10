using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WallpaperChanger.Core.Interfaces;
using WallpaperChanger.Core.Models;

namespace WallpaperChanger.Infrastructure.Settings;

/// <summary>
/// 設定を JSON ファイル（既定: %LOCALAPPDATA%\WallpaperChanger\settings.json）に保存する。
/// 現在の設定を保持するため、アプリケーション内で 1 インスタンスとして登録する。
/// 設定ファイルを読めない・壊れている場合も起動は止めず、既定値で動作する。
/// </summary>
internal sealed partial class JsonSettingsService : ISettingsService, IDisposable
{
    private readonly string _filePath;
    private readonly int _readAttempts;
    private readonly TimeSpan _readRetryDelay;
    private readonly ILogger<JsonSettingsService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AppSettings _current = new();
    private volatile string? _loadWarning;

    /// <summary>
    /// 読み込めなかった（または退避できなかった）設定ファイルが残っているため、上書きする前に退避する必要があるか。
    /// _gate の内側でのみ読み書きする。
    /// </summary>
    private bool _backupBeforeWrite;

    public JsonSettingsService(IOptions<SettingsStorageOptions> options, ILogger<JsonSettingsService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _filePath = Path.GetFullPath(options.Value.FilePath);
        _readAttempts = Math.Max(1, options.Value.ReadAttempts);
        _readRetryDelay = options.Value.ReadRetryDelay;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public event EventHandler<AppSettings>? SettingsChanged;

    public AppSettings Current => Volatile.Read(ref _current);

    public string? LoadWarning => _loadWarning;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        AppSettings loaded;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            loaded = await ReadAsync(cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _current, loaded);
        }
        finally
        {
            _gate.Release();
        }

        SettingsChanged?.Invoke(this, loaded);
    }

    public async Task UpdateAsync(Func<AppSettings, AppSettings> update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        AppSettings updated;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            updated = update(Current).Normalize();

            // 保存に失敗した場合はメモリ上の設定も変更しない
            await WriteAsync(updated, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _current, updated);
        }
        finally
        {
            _gate.Release();
        }

        SettingsChanged?.Invoke(this, updated);
    }

    public void Dispose() => _gate.Dispose();

    private async Task<AppSettings> ReadAsync(CancellationToken cancellationToken)
    {
        _loadWarning = null;
        _backupBeforeWrite = false;

        if (!File.Exists(_filePath))
        {
            LogSettingsFileNotFound(_filePath);
            return new AppSettings();
        }

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await DeserializeAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                // 存在確認の直後に削除された
                LogSettingsFileNotFound(_filePath);
                return new AppSettings();
            }
            catch (IOException ex) when (attempt < _readAttempts)
            {
                // 他のプロセスによる一時的なロックの可能性があるため、少し待って読み直す
                LogReadRetrying(ex, attempt, _readAttempts);
                await Task.Delay(_readRetryDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 内容は無事な可能性があるため、上書きして失わないよう保存前に退避する
                _backupBeforeWrite = true;
                LogSettingsUnreadable(ex, _filePath);
                _loadWarning = $"設定ファイルを読み込めなかったため、既定の設定で起動しました。設定を変更すると、元のファイルを退避してから保存します。（{ex.Message}）";
                return new AppSettings();
            }
            catch (JsonException ex)
            {
                return MoveCorruptedFile(ex);
            }
        }
    }

    private async Task<AppSettings> DeserializeAsync(CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            _filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);

        AppSettings? settings = await JsonSerializer
            .DeserializeAsync(stream, SettingsJsonContext.Default.AppSettings, cancellationToken)
            .ConfigureAwait(false);

        LogSettingsLoaded(_filePath);
        return (settings ?? new AppSettings()).Normalize();
    }

    /// <summary>破損した設定ファイルは退避して既定値で起動する（ユーザーが内容を確認・復旧できるよう削除はしない）。</summary>
    private AppSettings MoveCorruptedFile(JsonException exception)
    {
        string backupPath = CreateBackupPath("corrupt");
        try
        {
            File.Move(_filePath, backupPath, overwrite: true);
            LogSettingsCorrupted(exception, backupPath);
            _loadWarning = $"設定ファイルが壊れていたため、既定の設定で起動しました。壊れたファイルは次の場所に退避しました: {backupPath}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 退避できなくても起動は続け、最初に保存するときにもう一度退避を試みる
            _backupBeforeWrite = true;
            LogCorruptedBackupFailed(ex, _filePath);
            _loadWarning = "設定ファイルが壊れていたため、既定の設定で起動しました。設定を変更すると、壊れたファイルを退避してから保存します。";
        }

        return new AppSettings();
    }

    private async Task WriteAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException($"設定ファイルのパスが不正です: {_filePath}");
        Directory.CreateDirectory(directory);

        // 書き込み途中の異常終了でファイルが壊れないよう、一時ファイルに書いてから置き換える
        string tempPath = _filePath + ".tmp";
        await using (FileStream stream = new(
            tempPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true))
        {
            await JsonSerializer
                .SerializeAsync(stream, settings, SettingsJsonContext.Default.AppSettings, cancellationToken)
                .ConfigureAwait(false);
        }

        if (_backupBeforeWrite && File.Exists(_filePath))
        {
            // 読み込めなかった元のファイルを残す。退避できない場合は保存も中止する（設定を失わないことを優先する）
            string backupPath = CreateBackupPath("unreadable");
            File.Copy(_filePath, backupPath, overwrite: true);
            LogUnreadableFileBackedUp(backupPath);
        }

        File.Move(tempPath, _filePath, overwrite: true);
        _backupBeforeWrite = false;
        LogSettingsSaved(_filePath);
    }

    private string CreateBackupPath(string suffix) => $"{_filePath}.{DateTime.Now:yyyyMMddHHmmss}.{suffix}";

    [LoggerMessage(Level = LogLevel.Information, Message = "設定ファイルが無いため既定値を使用します: {FilePath}")]
    private partial void LogSettingsFileNotFound(string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "設定を読み込みました: {FilePath}")]
    private partial void LogSettingsLoaded(string filePath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "設定を保存しました: {FilePath}")]
    private partial void LogSettingsSaved(string filePath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "設定ファイルを読み込めないため再試行します ({Attempt}/{MaxAttempts})")]
    private partial void LogReadRetrying(Exception exception, int attempt, int maxAttempts);

    [LoggerMessage(Level = LogLevel.Error, Message = "設定ファイルを読み込めないため既定値で起動します。保存時に元のファイルを退避します: {FilePath}")]
    private partial void LogSettingsUnreadable(Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "設定ファイルが破損しているため既定値で起動します。破損ファイルの退避先: {BackupPath}")]
    private partial void LogSettingsCorrupted(Exception exception, string backupPath);

    [LoggerMessage(Level = LogLevel.Error, Message = "破損した設定ファイルを退避できませんでした。保存時に再度退避します: {FilePath}")]
    private partial void LogCorruptedBackupFailed(Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "読み込めなかった設定ファイルを退避しました: {BackupPath}")]
    private partial void LogUnreadableFileBackedUp(string backupPath);
}
