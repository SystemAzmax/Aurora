using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace WallpaperChanger.Infrastructure.Logging;

/// <summary>
/// ログを日付ごとのテキストファイルに書き込む ILoggerProvider。
/// 呼び出し元を待たせないよう、ログはキューに積んでバックグラウンドで書き込む。
/// </summary>
[ProviderAlias("File")]
internal sealed class FileLoggerProvider : ILoggerProvider
{
    private static readonly TimeSpan DisposeTimeout = TimeSpan.FromSeconds(5);

    private readonly FileLoggerOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly Channel<LogEntry> _queue;
    private readonly Task _writerTask;

    private DateOnly _currentDate;
    private StreamWriter? _writer;

    public FileLoggerProvider(IOptions<FileLoggerOptions> options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _queue = Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(Math.Max(1, _options.QueueCapacity))
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
        });
        _writerTask = Task.Run(WriteLoopAsync);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
        // 終了時は書き込み待ちのログを書き切ってから閉じる（長く待ちすぎないよう上限を設ける）
        _queue.Writer.TryComplete();
        if (!_writerTask.Wait(DisposeTimeout))
        {
            Debug.WriteLine("FileLoggerProvider: 終了時にログを書き切れませんでした。");
        }
    }

    internal bool IsEnabled(LogLevel level) => level != LogLevel.None && level >= _options.MinimumLevel;

    internal void Enqueue(LogEntry entry) => _queue.Writer.TryWrite(entry);

    internal DateTimeOffset Now => _timeProvider.GetLocalNow();

    private async Task WriteLoopAsync()
    {
        try
        {
            DeleteExpiredFiles();
            while (await _queue.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                while (_queue.Reader.TryRead(out LogEntry entry))
                {
                    Write(entry);
                }

                // まとめて書いた後に 1 回だけフラッシュする（ファイルを開けば直近のログを確認できる）
                FlushSafely();
            }
        }
        finally
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    private void Write(LogEntry entry)
    {
        try
        {
            StreamWriter writer = GetWriter(DateOnly.FromDateTime(entry.Timestamp.DateTime));
            writer.Write(Format(entry));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // ログの書き込み失敗は自分自身に記録できないため、デバッグ出力に残して処理を続ける（アプリは止めない）
            Debug.WriteLine($"FileLoggerProvider: ログを書き込めませんでした: {ex.Message}");
            _writer?.Dispose();
            _writer = null;
        }
    }

    private void FlushSafely()
    {
        try
        {
            _writer?.Flush();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"FileLoggerProvider: ログをフラッシュできませんでした: {ex.Message}");
            _writer?.Dispose();
            _writer = null;
        }
    }

    private StreamWriter GetWriter(DateOnly date)
    {
        if (_writer is not null && date == _currentDate)
        {
            return _writer;
        }

        // 日付が変わったら新しいファイルに切り替え、古いファイルを整理する
        bool isRollover = _writer is not null;
        _writer?.Dispose();
        Directory.CreateDirectory(_options.Directory);

        string path = Path.Combine(_options.Directory, $"{_options.FileNamePrefix}{date:yyyyMMdd}.log");
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        _currentDate = date;

        if (isRollover)
        {
            DeleteExpiredFiles();
        }

        return _writer;
    }

    private void DeleteExpiredFiles()
    {
        if (!Directory.Exists(_options.Directory))
        {
            return;
        }

        DateOnly today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
        DateOnly oldestToKeep = today.AddDays(-Math.Max(1, _options.RetentionDays) + 1);

        foreach (string file in Directory.EnumerateFiles(_options.Directory, $"{_options.FileNamePrefix}*.log"))
        {
            string datePart = Path.GetFileNameWithoutExtension(file)[_options.FileNamePrefix.Length..];
            if (!DateOnly.TryParseExact(datePart, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date)
                || date >= oldestToKeep)
            {
                continue;
            }

            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 古いログの削除は後片付けに過ぎないため、次回に再試行する
                Debug.WriteLine($"FileLoggerProvider: 古いログを削除できませんでした: {file}: {ex.Message}");
            }
        }
    }

    private static string Format(LogEntry entry)
    {
        var builder = new StringBuilder(256)
            .Append(entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
            .Append(" [").Append(GetLevelText(entry.Level)).Append("] ")
            .Append(entry.Category).Append(": ")
            .Append(entry.Message)
            .AppendLine();

        if (entry.Exception is not null)
        {
            builder.AppendLine(entry.Exception.ToString());
        }

        return builder.ToString();
    }

    private static string GetLevelText(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???",
    };

    internal readonly record struct LogEntry(
        DateTimeOffset Timestamp, LogLevel Level, string Category, string Message, Exception? Exception);

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => provider.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            if (!IsEnabled(logLevel))
            {
                return;
            }

            provider.Enqueue(new LogEntry(provider.Now, logLevel, category, formatter(state, exception), exception));
        }
    }
}
