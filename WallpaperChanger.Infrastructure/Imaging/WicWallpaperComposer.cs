using System.Runtime.ExceptionServices;
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
/// WPF の画像処理 (WIC) で画像をモニターの解像度の 1 枚に合成する。
/// 各画像は指定の方法（既定はタイル全体を覆うように拡大縮小し、はみ出した部分は中央で切り抜く）でタイルに収める。
/// Visual を使わず Freezable な BitmapSource のみで処理するため、任意のスレッドで実行できる。
/// </summary>
internal sealed partial class WicWallpaperComposer : IWallpaperComposer
{
    private const int BytesPerPixel = 4; // Bgr32

    private readonly WallpaperCompositionOptions _options;
    private readonly ImageSourceLimits _limits;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WicWallpaperComposer> _logger;

    public WicWallpaperComposer(
        IOptions<WallpaperCompositionOptions> options,
        IOptions<ImageSourceLimits> limits,
        TimeProvider timeProvider,
        ILogger<WicWallpaperComposer> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(limits);

        _options = options.Value;
        _limits = limits.Value;
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

    public Task<IReadOnlyDictionary<string, string>> ComposeSpanAsync(SpanCompositionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ImagePath);
        if (request.Monitors.Count == 0)
        {
            throw new ArgumentException("スパンに参加するモニターを 1 台以上指定してください。", nameof(request));
        }

        if (request.Monitors.Any(m => string.IsNullOrWhiteSpace(m.MonitorId) || m.Width <= 0 || m.Height <= 0
                                      || m.Bounds.Width <= 0 || m.Bounds.Height <= 0))
        {
            throw new ArgumentException("モニターの ID と大きさを指定してください。", nameof(request));
        }

        return Task.Run<IReadOnlyDictionary<string, string>>(() => ComposeSpan(request, cancellationToken), cancellationToken);
    }

    private Dictionary<string, string> ComposeSpan(SpanCompositionRequest request, CancellationToken cancellationToken)
    {
        SpanPlacement? placement = null;
        BitmapSource image = DecodeOriented(request.ImagePath, (width, height) =>
        {
            placement = SpanLayout.Calculate(width, height, [.. request.Monitors.Select(m => m.Bounds)], request.Fit);

            // 拡大する場合は元の大きさのままデコードし、モニターごとに切り抜いてから拡大する
            // （モニター全体を囲む大きさに拡大してからだと、横に長い並びでメモリを使いすぎるため）
            return placement.ScaledWidth <= width ? (placement.ScaledWidth, placement.ScaledHeight) : (width, height);
        });

        // 計算上の拡大縮小後の座標から、デコードした画像の座標への倍率
        double scaleX = (double)image.PixelWidth / placement!.ScaledWidth;
        double scaleY = (double)image.PixelHeight / placement.ScaledHeight;

        var outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < request.Monitors.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SpanMonitor monitor = request.Monitors[i];
            BitmapSource part = RenderSpanPart(image, placement.Parts[i], monitor, scaleX, scaleY, request.EmptyColor);
            string outputPath = Save(part, monitor.MonitorId);
            DeleteOldFiles(monitor.MonitorId, outputPath);
            outputs[monitor.MonitorId] = outputPath;
        }

        LogSpanComposed(request.Monitors.Count, request.ImagePath);
        return outputs;
    }

    /// <summary>1 台のモニターに映る範囲を切り抜き、モニターの解像度の画像にする。</summary>
    private static BitmapSource RenderSpanPart(
        BitmapSource image, SpanPart part, SpanMonitor monitor, double scaleX, double scaleY, int emptyColor)
    {
        int width = monitor.Bounds.Width;
        int height = monitor.Bounds.Height;
        int stride = width * BytesPerPixel;
        byte[] pixels = new byte[stride * height];
        FillBackground(pixels, emptyColor);

        if (!part.IsEmpty)
        {
            // 切り抜く範囲を、デコードした画像の座標に直す
            int x = Math.Clamp((int)Math.Floor(part.Source.X * scaleX), 0, image.PixelWidth - 1);
            int y = Math.Clamp((int)Math.Floor(part.Source.Y * scaleY), 0, image.PixelHeight - 1);
            int cropWidth = Math.Clamp((int)Math.Round(part.Source.Width * scaleX), 1, image.PixelWidth - x);
            int cropHeight = Math.Clamp((int)Math.Round(part.Source.Height * scaleY), 1, image.PixelHeight - y);
            BitmapSource piece = new CroppedBitmap(image, new Int32Rect(x, y, cropWidth, cropHeight));
            if (cropWidth != part.Source.Width || cropHeight != part.Source.Height)
            {
                piece = new TransformedBitmap(
                    piece, new ScaleTransform((double)part.Source.Width / cropWidth, (double)part.Source.Height / cropHeight));
            }

            var converted = new FormatConvertedBitmap(piece, PixelFormats.Bgr32, null, 0);

            // 拡大縮小の丸めで 1 ピクセルはみ出しても、モニターの範囲に収める
            int copyWidth = Math.Min(converted.PixelWidth, width - part.OffsetX);
            int copyHeight = Math.Min(converted.PixelHeight, height - part.OffsetY);
            int offset = (part.OffsetY * stride) + (part.OffsetX * BytesPerPixel);
            converted.CopyPixels(new Int32Rect(0, 0, copyWidth, copyHeight), pixels, stride, offset);
        }

        BitmapSource canvas = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, pixels, stride);

        // 仮想デスクトップ上の大きさと解像度が違う場合は、解像度に合わせる
        if (monitor.Width != width || monitor.Height != height)
        {
            canvas = new TransformedBitmap(canvas, new ScaleTransform((double)monitor.Width / width, (double)monitor.Height / height));
        }

        canvas.Freeze();
        return canvas;
    }

    private string Compose(WallpaperCompositionRequest request, CancellationToken cancellationToken)
    {
        IReadOnlyList<PixelRect> tiles = TileLayout.CalculateTiles(
            request.Width, request.Height, request.Columns, request.Rows, request.Gap);

        int stride = request.Width * BytesPerPixel;
        byte[] pixels = new byte[stride * request.Height];
        FillBackground(pixels, request.GapColor);
        DrawTiles(request, tiles, pixels, stride, cancellationToken);

        var composite = BitmapSource.Create(
            request.Width, request.Height, 96, 96, PixelFormats.Bgr32, null, pixels, stride);
        composite.Freeze();

        string outputPath = Save(composite, request.MonitorId);
        LogComposed(request.ImagePaths.Count, request.Width, request.Height, outputPath);
        DeleteOldFiles(request.MonitorId, outputPath);
        return outputPath;
    }

    /// <summary>
    /// 各マスの画像を並行して読み込み、合成用のバッファに書き込む。
    /// マス同士は重ならず、バッファの別々の範囲に書き込むため、並行して書き込んでよい。
    /// </summary>
    private void DrawTiles(
        WallpaperCompositionRequest request, IReadOnlyList<PixelRect> tiles, byte[] pixels, int stride, CancellationToken cancellationToken)
    {
        var parallelOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Max(1, _options.MaxParallelTileLoads),
        };

        try
        {
            Parallel.For(0, tiles.Count, parallelOptions, i =>
            {
                PixelRect tile = tiles[i];
                string? imagePath = request.ImagePaths[i % request.ImagePaths.Count];
                if (imagePath is null)
                {
                    // 画像を置かないマスは塗りつぶす（間隔部分と見分けられるよう別の色にする）
                    FillRect(pixels, stride, tile, request.EmptyColor);
                    return;
                }

                (FormatConvertedBitmap tileImage, int offsetX, int offsetY) = LoadTileImage(imagePath, tile.Width, tile.Height, request.Fit);
                if (tileImage.PixelWidth < tile.Width || tileImage.PixelHeight < tile.Height)
                {
                    // 全体表示・中央表示で画像がマスを覆わない部分は、画像の無いマスと同じ色にする
                    FillRect(pixels, stride, tile, request.EmptyColor);
                }

                int offset = ((tile.Y + offsetY) * stride) + ((tile.X + offsetX) * BytesPerPixel);
                tileImage.CopyPixels(Int32Rect.Empty, pixels, stride, offset);
            });
        }
        catch (AggregateException ex)
        {
            // 1 枚ずつ読んでいたときと同じく、原因の画像のパスを含む例外をそのまま送出する（複数あれば、そのうちの 1 つ）
            ExceptionDispatchInfo.Throw(ex.InnerExceptions[0]);
        }
    }

    /// <summary>
    /// 画像を読み込み、指定の方法でタイルに収まるように拡大縮小・切り抜きする（Bgr32）。
    /// 戻り値の画像はタイル以下の大きさで、タイル内のどこに置くかを合わせて返す。
    /// </summary>
    private (FormatConvertedBitmap Image, int OffsetX, int OffsetY) LoadTileImage(string imagePath, int tileWidth, int tileHeight, ImageFit fit)
    {
        ImagePlacement placement = default;
        BitmapSource scaled = DecodeOriented(imagePath, (width, height) =>
        {
            placement = TileLayout.CalculatePlacement(width, height, tileWidth, tileHeight, fit);
            return (placement.ScaledWidth, placement.ScaledHeight);
        });

        // タイルからはみ出す部分を切り抜き、合成バッファと同じ形式に変換する
        // （デコード結果の大きさが計算と 1 ピクセルずれても、画像とタイルの範囲に収める）
        int cropWidth = Math.Min(placement.Crop.Width, scaled.PixelWidth);
        int cropHeight = Math.Min(placement.Crop.Height, scaled.PixelHeight);
        var cropRect = new Int32Rect(
            Math.Clamp(placement.Crop.X, 0, scaled.PixelWidth - cropWidth),
            Math.Clamp(placement.Crop.Y, 0, scaled.PixelHeight - cropHeight),
            cropWidth,
            cropHeight);
        var cropped = new CroppedBitmap(scaled, cropRect);
        var converted = new FormatConvertedBitmap(cropped, PixelFormats.Bgr32, null, 0);
        converted.Freeze();
        return (converted, placement.OffsetX, placement.OffsetY);
    }

    /// <summary>
    /// 画像を読み込み、撮影時の向き（EXIF）に合わせて回転・反転する。
    /// 大きな写真でもメモリを抑えられるよう、<paramref name="decodeSize"/> が返す大きさで直接デコードする。
    /// </summary>
    /// <param name="decodeSize">向きを反映した元の幅・高さから、デコードする幅・高さ（向きを反映した後）を決める。</param>
    private BitmapSource DecodeOriented(string imagePath, Func<int, int, (int Width, int Height)> decodeSize)
    {
        try
        {
            using var stream = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (!_limits.IsAllowedFileSize(stream.Length))
            {
                throw new ImageTooLargeException(
                    $"画像のファイルサイズが上限 ({_limits.MaxFileBytes:N0} バイト) を超えるため読み込みません: {imagePath}");
            }

            // 1. ヘッダーだけ読んで元のサイズを得る（全体のデコードは縮小と同時に行う）
            BitmapFrame header = BitmapFrame.Create(
                stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);

            // 展開すると巨大になる画像でメモリを使い果たさないよう、デコードする前に断る
            if (!_limits.IsAllowedPixelCount(header.PixelWidth, header.PixelHeight))
            {
                throw new ImageTooLargeException(
                    $"画像のピクセル数 ({header.PixelWidth}×{header.PixelHeight}) が上限 ({_limits.MaxPixelCount:N0}) を超えるため読み込みません: {imagePath}");
            }

            // 撮影時の向き（EXIF）を反映した縦横で、デコードする大きさを決める
            int orientation = ExifOrientation.Read(header);
            bool swapsDimensions = ExifOrientation.SwapsDimensions(orientation);
            (int width, int height) = swapsDimensions
                ? decodeSize(header.PixelHeight, header.PixelWidth)
                : decodeSize(header.PixelWidth, header.PixelHeight);

            // 2. 決めた大きさで直接デコードする。
            //    デコードは保存されている向きのまま行うため、縦横が入れ替わる場合はデコードする大きさも入れ替える
            stream.Position = 0;
            var decoded = new BitmapImage();
            decoded.BeginInit();
            decoded.CacheOption = BitmapCacheOption.OnLoad;
            decoded.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            decoded.DecodePixelWidth = swapsDimensions ? height : width;
            decoded.DecodePixelHeight = swapsDimensions ? width : height;
            decoded.StreamSource = stream;
            decoded.EndInit();
            decoded.Freeze();
            return ExifOrientation.Apply(decoded, orientation);
        }
        catch (ImageTooLargeException)
        {
            // 理由とパスを含むメッセージのまま送出する
            throw;
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

    public void DeleteDisconnectedMonitorFiles(IReadOnlyCollection<string> connectedMonitorIds)
    {
        ArgumentNullException.ThrowIfNull(connectedMonitorIds);

        // 合成画像はモニターごとに最新の数枚だけを残すが、接続されなくなったモニターの分は合成されないため残り続ける
        HashSet<string> connectedKeys = [.. connectedMonitorIds.Select(GetMonitorKey)];
        DateTime threshold = _timeProvider.GetUtcNow().UtcDateTime - _options.DisconnectedMonitorRetention;
        try
        {
            var directory = new DirectoryInfo(_options.OutputDirectory);
            if (!directory.Exists)
            {
                return;
            }

            foreach (FileInfo file in directory.EnumerateFiles("*_*.jpg"))
            {
                string key = file.Name[..file.Name.IndexOf('_', StringComparison.Ordinal)];
                if (connectedKeys.Contains(key) || file.LastWriteTimeUtc > threshold)
                {
                    continue;
                }

                try
                {
                    file.Delete();
                    LogDisconnectedMonitorFileDeleted(file.FullName);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    LogDeleteFailed(ex, file.FullName);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 後片付けに過ぎないため、フォルダを読めなくても次回に再試行する
            LogDeleteFailed(ex, _options.OutputDirectory);
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

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Count} 台のモニターにまたがる画像を作りました: {ImagePath}")]
    private partial void LogSpanComposed(int count, string imagePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "接続されていないモニターの古い合成画像を削除しました: {FilePath}")]
    private partial void LogDisconnectedMonitorFileDeleted(string filePath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "古い合成画像を削除できませんでした: {FilePath}")]
    private partial void LogDeleteFailed(Exception exception, string filePath);
}
