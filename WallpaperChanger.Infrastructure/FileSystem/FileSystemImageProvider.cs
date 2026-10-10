using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WallpaperChanger.Core.Interfaces;
using WallpaperChanger.Core.Models;
using WallpaperChanger.Infrastructure.Imaging;

namespace WallpaperChanger.Infrastructure.FileSystem;

/// <summary>
/// ファイルシステムから対応形式（JPG / JPEG / PNG / BMP / WEBP）の画像を列挙する。
/// ファイルサイズが <see cref="ImageSourceLimits"/> の上限を超える画像は候補に含めない。
/// </summary>
internal sealed partial class FileSystemImageProvider(
    IOptions<ImageSourceLimits> limits,
    ILogger<FileSystemImageProvider> logger) : IImageProvider
{
    private readonly ImageSourceLimits _limits = (limits ?? throw new ArgumentNullException(nameof(limits))).Value;
    private readonly ILogger<FileSystemImageProvider> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public Task<IReadOnlyList<string>> GetImagesAsync(
        IEnumerable<string> folders,
        bool includeSubfolders,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folders);

        string[] targets = [.. folders];

        // 大量のファイルを含むフォルダでも呼び出し元（UI スレッド等）を塞がないようにする
        return Task.Run<IReadOnlyList<string>>(() => Enumerate(targets, includeSubfolders, cancellationToken), cancellationToken);
    }

    public bool Exists(string imagePath) =>
        !string.IsNullOrWhiteSpace(imagePath)
        && SupportedImageFormats.IsSupported(imagePath)
        && File.Exists(imagePath);

    private List<string> Enumerate(string[] folders, bool includeSubfolders, CancellationToken cancellationToken)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = includeSubfolders,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
            MatchCasing = MatchCasing.CaseInsensitive,
        };

        var images = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string folder in folders)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Directory.Exists(folder))
            {
                LogFolderNotFound(folder);
                continue;
            }

            try
            {
                // FileInfo のサイズは列挙時の情報から得られるため、ファイルを開かずに判定できる
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
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // ネットワークドライブの切断などで 1 フォルダが失敗しても、他のフォルダの画像は利用する
                LogFolderEnumerationFailed(ex, folder);
            }
        }

        List<string> sorted = [.. images];
        sorted.Sort(StringComparer.OrdinalIgnoreCase);
        LogImagesFound(sorted.Count, folders.Length);
        return sorted;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "壁紙フォルダが見つかりません: {Folder}")]
    private partial void LogFolderNotFound(string folder);

    [LoggerMessage(Level = LogLevel.Warning, Message = "壁紙フォルダの検索に失敗しました: {Folder}")]
    private partial void LogFolderEnumerationFailed(Exception exception, string folder);

    [LoggerMessage(Level = LogLevel.Debug, Message = "ファイルサイズが上限を超えるため候補から除外しました: {FilePath} ({Length} バイト)")]
    private partial void LogImageTooLarge(string filePath, long length);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{FolderCount} 個のフォルダから {Count} 件の画像が見つかりました")]
    private partial void LogImagesFound(int count, int folderCount);
}
