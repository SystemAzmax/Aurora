using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WallpaperChanger.Infrastructure.Imaging;

namespace WallpaperChanger.UI.Services;

internal sealed partial class ImagePreviewLoader(IOptions<ImageSourceLimits> limits, ILogger<ImagePreviewLoader> logger)
    : IImagePreviewLoader
{
    private readonly ImageSourceLimits _limits = (limits ?? throw new ArgumentNullException(nameof(limits))).Value;
    private readonly ILogger<ImagePreviewLoader> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public Task<ImageSource?> LoadAsync(string imagePath, int decodePixelWidth, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(decodePixelWidth);

        return Task.Run<ImageSource?>(() => Load(imagePath, decodePixelWidth), cancellationToken);
    }

    private BitmapImage? Load(string imagePath, int decodePixelWidth)
    {
        try
        {
            // ファイルをロックしないよう、ストリームから読み込んで即座に閉じる
            using var stream = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            // 展開すると巨大になる画像でメモリを使い果たさないよう、デコードする前にサイズを確かめる
            if (!_limits.IsAllowedFileSize(stream.Length))
            {
                LogPreviewTooLarge(imagePath);
                return null;
            }

            BitmapFrame header = BitmapFrame.Create(
                stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
            if (!_limits.IsAllowedPixelCount(header.PixelWidth, header.PixelHeight))
            {
                LogPreviewTooLarge(imagePath);
                return null;
            }

            stream.Position = 0;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.DecodePixelWidth = decodePixelWidth;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException)
        {
            // プレビューは補助機能のため、表示できない場合はプレースホルダーを表示する
            LogPreviewFailed(ex, imagePath);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "画像が大きすぎるためプレビューを表示しません: {ImagePath}")]
    private partial void LogPreviewTooLarge(string imagePath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "プレビュー画像を読み込めませんでした: {ImagePath}")]
    private partial void LogPreviewFailed(Exception exception, string imagePath);
}
