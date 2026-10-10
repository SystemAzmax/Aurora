using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WallpaperChanger.Core.Interfaces;
using WallpaperChanger.Core.Models;
using WallpaperChanger.Infrastructure.Imaging;

namespace WallpaperChanger.Infrastructure.FileSystem;

/// <summary>
/// ファイルシステムから対応形式（JPG / JPEG / PNG / BMP / WEBP）の画像を列挙する。
/// ファイルサイズが <see cref="ImageSourceLimits"/> の上限を超える画像は候補に含めない。
/// 列挙結果はフォルダごとにキャッシュし、フォルダの変更を検知したとき、または
/// <see cref="ImageProviderOptions.CacheDuration"/> が経過したときに列挙し直す。
/// キャッシュを保持するため、アプリケーション内で 1 インスタンスとして登録する。
/// </summary>
internal sealed partial class FileSystemImageProvider : IImageProvider, IDisposable
{
    private static readonly EnumerationOptions FlatEnumeration = CreateEnumerationOptions(recurse: false);
    private static readonly EnumerationOptions RecursiveEnumeration = CreateEnumerationOptions(recurse: true);

    private readonly ImageSourceLimits _limits;
    private readonly ImageProviderOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<FileSystemImageProvider> _logger;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, FolderCache> _cache = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public FileSystemImageProvider(
        IOptions<ImageSourceLimits> limits,
        IOptions<ImageProviderOptions> options,
        TimeProvider timeProvider,
        ILogger<FileSystemImageProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(options);

        _limits = limits.Value;
        _options = options.Value;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<IReadOnlyList<string>> GetImagesAsync(
        IEnumerable<string> folders,
        bool includeSubfolders,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folders);

        string[] targets = [.. folders];

        // 大量のファイルを含むフォルダでも呼び出し元（UI スレッド等）を塞がないようにする
        return Task.Run<IReadOnlyList<string>>(() => GetImages(targets, includeSubfolders, cancellationToken), cancellationToken);
    }

    public bool Exists(string imagePath) =>
        !string.IsNullOrWhiteSpace(imagePath)
        && SupportedImageFormats.IsSupported(imagePath)
        && File.Exists(imagePath);

    public void Invalidate()
    {
        lock (_gate)
        {
            foreach (FolderCache entry in _cache.Values)
            {
                entry.Dispose();
            }

            _cache.Clear();
        }

        LogInvalidated();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            foreach (FolderCache entry in _cache.Values)
            {
                entry.Dispose();
            }

            _cache.Clear();
        }
    }

    private List<string> GetImages(string[] folders, bool includeSubfolders, CancellationToken cancellationToken)
    {
        RemoveStaleEntries();

        var images = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string folder in folders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            images.UnionWith(GetFolderImages(folder, includeSubfolders, cancellationToken));
        }

        List<string> sorted = [.. images];
        sorted.Sort(StringComparer.OrdinalIgnoreCase);
        LogImagesFound(sorted.Count, folders.Length);
        return sorted;
    }

    private List<string> GetFolderImages(string folder, bool includeSubfolders, CancellationToken cancellationToken)
    {
        string key = $"{(includeSubfolders ? 'R' : 'F')}|{folder}";
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out FolderCache? cached) && IsUsable(cached))
            {
                return cached.Images;
            }
        }

        if (!Directory.Exists(folder))
        {
            LogFolderNotFound(folder);
            return [];
        }

        // 列挙中に追加・削除されたファイルも検知できるよう、列挙より先に監視を始める
        var entry = new FolderCache(_timeProvider.GetTimestamp());
        try
        {
            if (_options.WatchForChanges)
            {
                entry.Watcher = TryCreateWatcher(folder, includeSubfolders, entry);
            }

            bool isComplete = Enumerate(folder, includeSubfolders, entry.Images, cancellationToken);
            if (!isComplete)
            {
                // 一部しか列挙できなかった結果は使い回さない（次回に列挙し直す）
                entry.Dispose();
                return entry.Images;
            }
        }
        catch
        {
            entry.Dispose();
            throw;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                entry.Dispose();
                return entry.Images;
            }

            if (_cache.Remove(key, out FolderCache? old))
            {
                old.Dispose();
            }

            _cache[key] = entry;
        }

        LogFolderEnumerated(folder, entry.Images.Count, entry.Watcher is not null);
        return entry.Images;
    }

    /// <summary>変更が検知された、または期限が切れたキャッシュを破棄する（使われなくなったフォルダの監視も止める）。</summary>
    private void RemoveStaleEntries()
    {
        lock (_gate)
        {
            foreach ((string key, FolderCache entry) in _cache.Where(p => !IsUsable(p.Value)).ToList())
            {
                _cache.Remove(key);
                entry.Dispose();
            }
        }
    }

    private bool IsUsable(FolderCache entry) =>
        !entry.IsDirty && _timeProvider.GetElapsedTime(entry.LoadedAt) < _options.CacheDuration;

    private bool Enumerate(string folder, bool includeSubfolders, List<string> images, CancellationToken cancellationToken)
    {
        try
        {
            // FileInfo のサイズは列挙時の情報から得られるため、ファイルを開かずに判定できる
            EnumerationOptions options = includeSubfolders ? RecursiveEnumeration : FlatEnumeration;
            foreach (FileInfo file in new DirectoryInfo(folder).EnumerateFiles("*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!SupportedImageFormats.IsSupported(file.FullName))
                {
                    continue;
                }

                if (!_limits.IsAllowedFileSize(file.Length))
                {
                    LogImageTooLarge(file.FullName, file.Length);
                    continue;
                }

                images.Add(file.FullName);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // ネットワークドライブの切断などで 1 フォルダが失敗しても、他のフォルダの画像は利用する
            LogFolderEnumerationFailed(ex, folder);
            return false;
        }
    }

    private FileSystemWatcher? TryCreateWatcher(string folder, bool includeSubfolders, FolderCache entry)
    {
        FileSystemWatcher? watcher = null;
        try
        {
            watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = includeSubfolders,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
            };

            // どの変更も列挙し直すきっかけにする。通知の取りこぼし（Error）やネットワークの切断でも列挙し直す
            watcher.Created += (_, _) => entry.MarkDirty();
            watcher.Deleted += (_, _) => entry.MarkDirty();
            watcher.Renamed += (_, _) => entry.MarkDirty();
            watcher.Error += (_, _) => entry.MarkDirty();
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // 監視できないフォルダ（一部のネットワーク共有など）は、CacheDuration ごとの列挙し直しだけで追従する
            watcher?.Dispose();
            LogWatchUnavailable(ex, folder);
            return null;
        }
    }

    private static EnumerationOptions CreateEnumerationOptions(bool recurse) => new()
    {
        RecurseSubdirectories = recurse,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        MatchCasing = MatchCasing.CaseInsensitive,
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "壁紙フォルダが見つかりません: {Folder}")]
    private partial void LogFolderNotFound(string folder);

    [LoggerMessage(Level = LogLevel.Warning, Message = "壁紙フォルダの検索に失敗しました: {Folder}")]
    private partial void LogFolderEnumerationFailed(Exception exception, string folder);

    [LoggerMessage(Level = LogLevel.Debug, Message = "ファイルサイズが上限を超えるため候補から除外しました: {FilePath} ({Length} バイト)")]
    private partial void LogImageTooLarge(string filePath, long length);

    [LoggerMessage(Level = LogLevel.Debug, Message = "壁紙フォルダを列挙しました: {Folder} ({Count} 件、変更の監視: {IsWatching})")]
    private partial void LogFolderEnumerated(string folder, int count, bool isWatching);

    [LoggerMessage(Level = LogLevel.Information, Message = "壁紙フォルダの変更を監視できないため、一定時間ごとに列挙し直します: {Folder}")]
    private partial void LogWatchUnavailable(Exception exception, string folder);

    [LoggerMessage(Level = LogLevel.Debug, Message = "壁紙フォルダの列挙結果のキャッシュを破棄しました")]
    private partial void LogInvalidated();

    [LoggerMessage(Level = LogLevel.Debug, Message = "{FolderCount} 個のフォルダから {Count} 件の画像が見つかりました")]
    private partial void LogImagesFound(int count, int folderCount);

    /// <summary>1 フォルダ分の列挙結果と、その変更の監視。</summary>
    private sealed class FolderCache(long loadedAt) : IDisposable
    {
        private volatile bool _isDirty;

        /// <summary>列挙を始めた時刻（<see cref="TimeProvider.GetTimestamp"/>）。</summary>
        public long LoadedAt { get; } = loadedAt;

        /// <summary>列挙結果。キャッシュに登録した後は変更しない。</summary>
        public List<string> Images { get; } = [];

        public FileSystemWatcher? Watcher { get; set; }

        public bool IsDirty => _isDirty;

        public void MarkDirty() => _isDirty = true;

        public void Dispose() => Watcher?.Dispose();
    }
}
