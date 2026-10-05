using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WallpaperChanger.Core.Interfaces;
using WallpaperChanger.Core.Models;
using WallpaperChanger.Core.Services;

namespace WallpaperChanger.Infrastructure.Imaging;

/// <summary>
/// WPF の画像処理 (WIC) で複数の画像を 1 枚に合成する。
/// 各画像はタイル全体を覆うように拡大縮小し、はみ出した部分は中央で切り抜く。
/// Visual を使わず Freezable な BitmapSource のみで処理するため、任意のスレッドで実行できる。
/// </summary>
internal sealed partial class WicWallpaperComposer : IWallpaperComposer
{
    private const int BytesPerPixel = 4; // Bgr32

    private readonly WallpaperCompositionOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WicWallpaperComposer> _logger;

    public WicWallpaperComposer(
        IOptions<WallpaperCompositionOptions> options,
        TimeProvider timeProvider,
        ILogger<WicWallpaperComposer> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<string> ComposeAsync(WallpaperCompositionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.MonitorId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.Height);
        if (request.ImagePaths.Count == 0)
        {
            throw new ArgumentException("合成する画像を 1 枚以上指定してください。", nameof(request));
        }

        // 画像のデコードと縮小は重いため、呼び出し元スレッドを塞がない
        return Task.Run(() => Compose(request, cancellationToken), cancellationToken);
    }

    private string Compose(WallpaperCompositionRequest request, CancellationToken cancellationToken)
    {
        IReadOnlyList<PixelRect> tiles = TileLayout.CalculateTiles(
            request.Width, request.Height, request.Columns, request.Rows, _options.TileGap);

        int stride = request.Width * BytesPerPixel;
        byte[] pixels = new byte[stride * request.Height];
        FillBackground(pixels, _options.BackgroundColor);

        for (int i = 0; i < tiles.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            PixelRect tile = tiles[i];
            string? imagePath = request.ImagePaths[i % request.ImagePaths.Count];
            if (imagePath is null)
            {
                // 画像を置かないマスは塗りつぶす（間隔部分と見分けられるよう別の色にする）
                FillRect(pixels, stride, tile, _options.EmptyTileColor);
                continue;
            }

            FormatConvertedBitmap tileImage = LoadTileImage(imagePath, tile.Width, tile.Height);

            int offset = (tile.Y * stride) + (tile.X * BytesPerPixel);
            tileImage.CopyPixels(Int32Rect.Empty, pixels, stride, offset);
        }

        var composite = BitmapSource.Create(
            request.Width, request.Height, 96, 96, PixelFormats.Bgr32, null, pixels, stride);
        composite.Freeze();

        string outputPath = Save(composite, request.MonitorId);
        LogComposed(request.ImagePaths.Count, request.Width, request.Height, outputPath);
        DeleteOldFiles(request.MonitorId, outputPath);
        return outputPath;
    }

    /// <summary>画像を読み込み、タイルと同じサイズ（Bgr32）に拡大縮小・切り抜きする。</summary>
    private static FormatConvertedBitmap LoadTileImage(string imagePath, int tileWidth, int tileHeight)
    {
        try
        {
            using var stream = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            // 1. ヘッダーだけ読んで元のサイズを得る（全体のデコードは縮小と同時に行う）
            BitmapFrame header = BitmapFrame.Create(
                stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
            FillScaling fill = TileLayout.CalculateFill(header.PixelWidth, header.PixelHeight, tileWidth, tileHeight);

            // 2. タイルを覆うサイズで直接デコードする（大きな写真でもメモリを抑えられる）
            stream.Position = 0;
            var scaled = new BitmapImage();
            scaled.BeginInit();
            scaled.CacheOption = BitmapCacheOption.OnLoad;
            scaled.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            scaled.DecodePixelWidth = fill.ScaledWidth;
            scaled.DecodePixelHeight = fill.ScaledHeight;
            scaled.StreamSource = stream;
            scaled.EndInit();
            scaled.Freeze();

            // 3. 中央を切り抜き、合成バッファと同じ形式に変換する
            var cropRect = new Int32Rect(
                Math.Clamp(fill.Crop.X, 0, Math.Max(0, scaled.PixelWidth - tileWidth)),
                Math.Clamp(fill.Crop.Y, 0, Math.Max(0, scaled.PixelHeight - tileHeight)),
                Math.Min(tileWidth, scaled.PixelWidth),
                Math.Min(tileHeight, scaled.PixelHeight));
            var cropped = new CroppedBitmap(scaled, cropRect);
            var converted = new FormatConvertedBitmap(cropped, PixelFormats.Bgr32, null, 0);
            converted.Freeze();
            return converted;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                                       or FileFormatException or ArgumentException or InvalidOperationException)
        {
            // どの画像が原因かを呼び出し元（ログ・通知）で分かるようにして送出する
            throw new InvalidOperationException($"画像を読み込めませんでした: {imagePath}", ex);
        }
    }

    private string Save(BitmapSource composite, string monitorId)
    {
        Directory.CreateDirectory(_options.OutputDirectory);

        // 同じパスに上書きすると Windows が壁紙を更新しないことがあるため、毎回別名で保存する
        string timestamp = _timeProvider.GetLocalNow().ToString("yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture);
        string outputPath = Path.Combine(_options.OutputDirectory, $"{GetMonitorKey(monitorId)}_{timestamp}.jpg");
        string tempPath = outputPath + ".tmp";

        var encoder = new JpegBitmapEncoder { QualityLevel = Math.Clamp(_options.JpegQuality, 1, 100) };
        encoder.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(composite, PixelFormats.Bgr24, null, 0)));
        using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            encoder.Save(stream);
        }

        File.Move(tempPath, outputPath, overwrite: true);
        return outputPath;
    }

    private void DeleteOldFiles(string monitorId, string currentPath)
    {
        int keep = Math.Max(2, _options.FilesToKeepPerMonitor);
        IEnumerable<FileInfo> oldFiles = new DirectoryInfo(_options.OutputDirectory)
            .EnumerateFiles($"{GetMonitorKey(monitorId)}_*.jpg")
            .Where(f => !string.Equals(f.FullName, currentPath, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.Name, StringComparer.Ordinal)
            .Skip(keep - 1);

        foreach (FileInfo file in oldFiles)
        {
            try
            {
                file.Delete();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 古い合成画像の削除は後片付けに過ぎないため、失敗しても次回に再試行する
                LogDeleteFailed(ex, file.FullName);
            }
        }
    }

    private static void FillRect(byte[] pixels, int stride, PixelRect rect, int rgb)
    {
        byte b = (byte)(rgb & 0xFF);
        byte g = (byte)((rgb >> 8) & 0xFF);
        byte r = (byte)((rgb >> 16) & 0xFF);
        for (int y = rect.Y; y < rect.Y + rect.Height; y++)
        {
            int rowStart = (y * stride) + (rect.X * BytesPerPixel);
            for (int x = 0; x < rect.Width; x++)
            {
                int i = rowStart + (x * BytesPerPixel);
                pixels[i] = b;
                pixels[i + 1] = g;
                pixels[i + 2] = r;
                pixels[i + 3] = 0xFF;
            }
        }
    }

    private static void FillBackground(byte[] pixels, int rgb)
    {
        byte b = (byte)(rgb & 0xFF);
        byte g = (byte)((rgb >> 8) & 0xFF);
        byte r = (byte)((rgb >> 16) & 0xFF);
        for (int i = 0; i < pixels.Length; i += BytesPerPixel)
        {
            pixels[i] = b;
            pixels[i + 1] = g;
            pixels[i + 2] = r;
            pixels[i + 3] = 0xFF;
        }
    }

    /// <summary>モニター ID（デバイスパス）をファイル名に使える短いキーにする。</summary>
    internal static string GetMonitorKey(string monitorId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(monitorId.ToUpperInvariant())))[..16];

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Count} 枚の画像を合成しました ({Width}×{Height}): {OutputPath}")]
    private partial void LogComposed(int count, int width, int height, string outputPath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "古い合成画像を削除できませんでした: {FilePath}")]
    private partial void LogDeleteFailed(Exception exception, string filePath);
}
