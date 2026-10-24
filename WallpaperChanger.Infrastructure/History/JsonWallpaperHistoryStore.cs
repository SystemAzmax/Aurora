using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WallpaperChanger.Core.Interfaces;
using WallpaperChanger.Core.Models;

namespace WallpaperChanger.Infrastructure.History;

/// <summary>
/// 壁紙の履歴を JSON ファイル（既定: %LOCALAPPDATA%\WallpaperChanger\history.json）に保存する。
/// 履歴は失っても設定ほどは困らないため、読めない・壊れているファイルは退避せずに無視し（次の保存で置き換わる）、
/// 保存の失敗もログに記録するだけにする。
/// 呼び出し側（WallpaperRotationService）が排他するため、同時には呼ばれない前提とする。
/// </summary>
internal sealed partial class JsonWallpaperHistoryStore : IWallpaperHistoryStore
{
    private readonly string _filePath;
    private readonly ILogger<JsonWallpaperHistoryStore> _logger;

    public JsonWallpaperHistoryStore(IOptions<HistoryStorageOptions> options, ILogger<JsonWallpaperHistoryStore> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _filePath = Path.GetFullPath(options.Value.FilePath);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<WallpaperHistorySnapshot>> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using FileStream stream = new(
                _filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);

            WallpaperHistoryFile? file = await JsonSerializer
                .DeserializeAsync(stream, HistoryJsonContext.Default.WallpaperHistoryFile, cancellationToken)
                .ConfigureAwait(false);

            IReadOnlyList<WallpaperHistorySnapshot> histories = file?.Monitors ?? [];
            LogHistoryLoaded(_filePath, histories.Count);
            return histories;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            LogHistoryFileNotFound(_filePath);
            return [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            // ArgumentException は、画像が 1 枚も無い組など WallpaperSelection として不正な内容のとき
            LogHistoryUnreadable(ex, _filePath);
            return [];
        }
    }

    public async Task SaveAsync(IReadOnlyList<WallpaperHistorySnapshot> histories, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(histories);

        string directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException($"履歴ファイルのパスが不正です: {_filePath}");
        try
        {
            Directory.CreateDirectory(directory);

            // 書き込み途中の異常終了でファイルが壊れないよう、一時ファイルに書いてから置き換える
            string tempPath = _filePath + ".tmp";
            await using (FileStream stream = new(
                tempPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true))
            {
                await JsonSerializer
                    .SerializeAsync(stream, new WallpaperHistoryFile(histories), HistoryJsonContext.Default.WallpaperHistoryFile, cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(tempPath, _filePath, overwrite: true);
            LogHistorySaved(_filePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogHistorySaveFailed(ex, _filePath);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "壁紙の履歴を読み込みました ({Count} 台分): {FilePath}")]
    private partial void LogHistoryLoaded(string filePath, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "壁紙の履歴ファイルが無いため、履歴の無い状態で始めます: {FilePath}")]
    private partial void LogHistoryFileNotFound(string filePath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "壁紙の履歴ファイルを読み込めないため、履歴の無い状態で始めます（次の保存で置き換えます）: {FilePath}")]
    private partial void LogHistoryUnreadable(Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "壁紙の履歴を保存しました: {FilePath}")]
    private partial void LogHistorySaved(string filePath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "壁紙の履歴を保存できませんでした: {FilePath}")]
    private partial void LogHistorySaveFailed(Exception exception, string filePath);
}
