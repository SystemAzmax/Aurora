using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WallpaperChanger.Core.Interfaces;
using WallpaperChanger.Core.Models;

namespace WallpaperChanger.Infrastructure.Settings;

/// <summary>
/// 設定を JSON ファイル（既定: %LOCALAPPDATA%\WallpaperChanger\settings.json）に保存する。
/// 現在の設定を保持するため、アプリケーション内で 1 インスタンスとして登録する。
/// </summary>
internal sealed partial class JsonSettingsService : ISettingsService, IDisposable
{
    private readonly string _filePath;
    private readonly ILogger<JsonSettingsService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AppSettings _current = new();

    public JsonSettingsService(IOptions<SettingsStorageOptions> options, ILogger<JsonSettingsService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _filePath = Path.GetFullPath(options.Value.FilePath);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public event EventHandler<AppSettings>? SettingsChanged;

    public AppSettings Current => Volatile.Read(ref _current);

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
        if (!File.Exists(_filePath))
        {
            LogSettingsFileNotFound(_filePath);
            return new AppSettings();
        }

        try
        {
            await using FileStream stream = new(
                _filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);

            AppSettings? settings = await JsonSerializer
                .DeserializeAsync(stream, SettingsJsonContext.Default.AppSettings, cancellationToken)
                .ConfigureAwait(false);

            LogSettingsLoaded(_filePath);
            return (settings ?? new AppSettings()).Normalize();
        }
        catch (JsonException ex)
        {
            // 破損した設定ファイルは退避して既定値で起動する（ユーザーが内容を確認・復旧できるよう削除はしない）
            string backupPath = $"{_filePath}.{DateTime.Now:yyyyMMddHHmmss}.corrupt";
            File.Move(_filePath, backupPath, overwrite: true);
            LogSettingsCorrupted(ex, backupPath);
            return new AppSettings();
        }
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

        File.Move(tempPath, _filePath, overwrite: true);
        LogSettingsSaved(_filePath);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "設定ファイルが無いため既定値を使用します: {FilePath}")]
    private partial void LogSettingsFileNotFound(string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "設定を読み込みました: {FilePath}")]
    private partial void LogSettingsLoaded(string filePath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "設定を保存しました: {FilePath}")]
    private partial void LogSettingsSaved(string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "設定ファイルが破損しているため既定値で起動します。破損ファイルの退避先: {BackupPath}")]
    private partial void LogSettingsCorrupted(Exception exception, string backupPath);
}
