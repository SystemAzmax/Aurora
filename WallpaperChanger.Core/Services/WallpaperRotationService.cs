using Microsoft.Extensions.Logging;
using WallpaperChanger.Core.Interfaces;
using WallpaperChanger.Core.Models;

namespace WallpaperChanger.Core.Services;

/// <summary>
/// モニターごとの設定と履歴に基づいて壁紙を切り替える。
/// 履歴を保持するため、アプリケーション内で 1 インスタンスとして登録する。
/// </summary>
public sealed partial class WallpaperRotationService : IWallpaperRotationService, IDisposable
{
    private readonly IMonitorService _monitorService;
    private readonly IWallpaperService _wallpaperService;
    private readonly IImageProvider _imageProvider;
    private readonly IWallpaperComposer _composer;
    private readonly ISettingsService _settingsService;
    private readonly Dictionary<ImageSelectionMode, IImageSelector> _selectors;
    private readonly ILogger<WallpaperRotationService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, WallpaperHistory> _histories = new(StringComparer.OrdinalIgnoreCase);

    public WallpaperRotationService(
        IMonitorService monitorService,
        IWallpaperService wallpaperService,
        IImageProvider imageProvider,
        IWallpaperComposer composer,
        ISettingsService settingsService,
        IEnumerable<IImageSelector> selectors,
        ILogger<WallpaperRotationService> logger)
    {
        ArgumentNullException.ThrowIfNull(selectors);

        _monitorService = monitorService ?? throw new ArgumentNullException(nameof(monitorService));
        _wallpaperService = wallpaperService ?? throw new ArgumentNullException(nameof(wallpaperService));
        _imageProvider = imageProvider ?? throw new ArgumentNullException(nameof(imageProvider));
        _composer = composer ?? throw new ArgumentNullException(nameof(composer));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _selectors = selectors.ToDictionary(s => s.Mode);
    }

    public event EventHandler<WallpaperChangedEventArgs>? WallpaperChanged;

    public Task<WallpaperChangeResult> NextAsync(string? monitorId = null, CancellationToken cancellationToken = default) =>
        ExecuteForMonitorsAsync(monitorId, NextForMonitorAsync, cancellationToken);

    public Task<WallpaperChangeResult> PreviousAsync(string? monitorId = null, CancellationToken cancellationToken = default) =>
        ExecuteForMonitorsAsync(monitorId, PreviousForMonitorAsync, cancellationToken);

    public Task<WallpaperChangeResult> ReapplyAsync(string monitorId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(monitorId);
        return ExecuteForMonitorsAsync(monitorId, ReapplyForMonitorAsync, cancellationToken);
    }

    public void Dispose() => _gate.Dispose();

    private async Task<WallpaperChangeResult> ExecuteForMonitorsAsync(
        string? monitorId,
        Func<MonitorInfo, CancellationToken, Task<WallpaperChangeStatus>> action,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            IReadOnlyList<MonitorInfo> monitors = await _monitorService.GetMonitorsAsync(cancellationToken).ConfigureAwait(false);
            List<MonitorInfo> targets = monitorId is null
                ? [.. monitors]
                : [.. monitors.Where(m => string.Equals(m.Id, monitorId, StringComparison.OrdinalIgnoreCase))];

            if (targets.Count == 0)
            {
                LogNoTargetMonitor(monitorId ?? "(all)");
                return WallpaperChangeResult.Empty;
            }

            // 1 台の失敗で他のモニターの切り替えを止めないよう、例外は集約して最後に送出する
            List<Exception> errors = [];
            List<MonitorChangeResult> results = [];
            foreach (MonitorInfo monitor in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    WallpaperChangeStatus status = await action(monitor, cancellationToken).ConfigureAwait(false);
                    results.Add(new MonitorChangeResult(monitor, status));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogMonitorChangeFailed(ex, monitor.DisplayName);
                    errors.Add(ex);
                }
            }

            // 接続されなくなったモニターの合成画像は、そのモニターでは合成されないため、ここで後片付けする
            _composer.DeleteDisconnectedMonitorFiles([.. monitors.Select(m => m.Id)]);

            if (errors.Count > 0)
            {
                throw new AggregateException("一部のモニターで壁紙の変更に失敗しました。", errors);
            }

            return new WallpaperChangeResult(results);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<WallpaperChangeStatus> NextForMonitorAsync(MonitorInfo monitor, CancellationToken cancellationToken)
    {
        MonitorSettings settings = _settingsService.Current.GetMonitor(monitor.Id);
        if (!settings.HasAnyFolder())
        {
            LogNoFolders(monitor.DisplayName);
            return WallpaperChangeStatus.NoFolders;
        }

        WallpaperHistory history = GetHistory(monitor.Id);

        // 「前へ」で戻った後は、まず履歴を進める（レイアウトを変えた後は新しく選び直す）
        if (history.Forward is { } forward && forward.Layout == settings.Layout && AllImagesExist(forward))
        {
            history.TryMoveForward(out _);
            await ApplyAsync(monitor, forward, cancellationToken).ConfigureAwait(false);
            return WallpaperChangeStatus.Changed;
        }

        // 起動時の壁紙にも「前へ」で戻れるよう、最初の 1 件として記録する
        if (history.IsEmpty)
        {
            string? initial = await _wallpaperService.GetWallpaperAsync(monitor.Id, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(initial))
            {
                history.Push(WallpaperSelection.ForSingleImage(initial));
            }
        }

        IReadOnlyList<string?> current = history.Current?.Images ?? [];
        IReadOnlyList<string?>? next = await SelectImagesAsync(settings, current, cancellationToken).ConfigureAwait(false);

        // キャッシュされた候補が、その後に削除・移動されていた場合は列挙し直して選び直す
        if (next is not null && !next.OfType<string>().All(_imageProvider.Exists))
        {
            LogStaleImageList(monitor.DisplayName);
            _imageProvider.Invalidate();
            next = await SelectImagesAsync(settings, current, cancellationToken).ConfigureAwait(false);
        }

        if (next is null)
        {
            LogNoImages(monitor.DisplayName);
            return WallpaperChangeStatus.NoImages;
        }

        var selection = new WallpaperSelection(settings.Layout, next);
        await ApplyAsync(monitor, selection, cancellationToken).ConfigureAwait(false);
        history.Push(selection);
        return WallpaperChangeStatus.Changed;
    }

    /// <summary>
    /// 次に表示する画像を選ぶ。null の要素は画像を置かないマス（黒）を表す。
    /// 設定済みのフォルダに対応画像が 1 枚も無い場合は null を返し、今の表示を残す。
    /// </summary>
    /// <remarks>
    /// 分割表示では、どの区画にも専用フォルダが無ければ全マスを 1 枚表示のフォルダから選ぶ。
    /// 1 つでも専用フォルダがあれば、専用フォルダの無い区画のマスは黒にする。
    /// 同じフォルダを使うマス（16 分割の同じ区画の 4 マスなど）はまとめて選び、マス同士で同じ画像が並ばないようにする。
    /// </remarks>
    private async Task<IReadOnlyList<string?>?> SelectImagesAsync(
        MonitorSettings settings,
        IReadOnlyList<string?> current,
        CancellationToken cancellationToken)
    {
        IImageSelector selector = GetSelector(settings.SelectionMode);
        IReadOnlyList<string>[] tileFolders = GetTileFolders(settings);

        // 1. 取得元（フォルダの組）ごとに画像を列挙する
        var pools = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (IReadOnlyList<string> folders in tileFolders.Where(f => f.Count > 0))
        {
            string key = GetPoolKey(folders);
            if (!pools.ContainsKey(key))
            {
                pools[key] = await _imageProvider
                    .GetImagesAsync(folders, settings.IncludeSubfolders, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        // フォルダの移動・ドライブの切断などで画像が見つからない場合は、黒にせず今の表示を残す
        if (pools.Count == 0 || pools.Values.Any(p => p.Count == 0))
        {
            return null;
        }

        // 2. 同じ取得元のマスをまとめて選ぶ（フォルダの無いマスは null = 黒のまま）
        string?[] result = new string?[tileFolders.Length];
        IEnumerable<IGrouping<string, int>> groups = Enumerable.Range(0, tileFolders.Length)
            .Where(i => tileFolders[i].Count > 0)
            .GroupBy(i => GetPoolKey(tileFolders[i]), StringComparer.OrdinalIgnoreCase);

        foreach (IGrouping<string, int> group in groups)
        {
            int[] tiles = [.. group];
            string[] groupCurrent = [.. tiles.Where(i => i < current.Count).Select(i => current[i]).OfType<string>()];
            IReadOnlyList<string> picked = selector.SelectNext(pools[group.Key], groupCurrent, tiles.Length);
            for (int j = 0; j < tiles.Length; j++)
            {
                result[tiles[j]] = picked[j];
            }
        }

        return result;
    }

    /// <summary>マスごとの画像の取得元。空のリストはそのマスに画像を置かないことを表す。</summary>
    private static IReadOnlyList<string>[] GetTileFolders(MonitorSettings settings)
    {
        if (settings.Layout == WallpaperLayout.SingleImage)
        {
            return [settings.Folders];
        }

        return [.. Enumerable.Range(0, settings.Layout.GetTileCount()).Select(settings.GetEffectiveTileFolders)];
    }

    private static string GetPoolKey(IReadOnlyList<string> folders) => string.Join('|', folders);

    private async Task<WallpaperChangeStatus> ReapplyForMonitorAsync(MonitorInfo monitor, CancellationToken cancellationToken)
    {
        MonitorSettings settings = _settingsService.Current.GetMonitor(monitor.Id);

        // 1 枚表示は OS が新しい解像度に合わせて拡大縮小するため、作り直す必要がない
        if (settings.Layout == WallpaperLayout.SingleImage)
        {
            return WallpaperChangeStatus.NotRequired;
        }

        WallpaperHistory history = GetHistory(monitor.Id);
        if (history.Current is { } current && current.Layout == settings.Layout && AllImagesExist(current))
        {
            // 同じ画像の組を、新しい解像度で合成し直す（履歴は進めない）
            await ApplyAsync(monitor, current, cancellationToken).ConfigureAwait(false);
            return WallpaperChangeStatus.Changed;
        }

        return await NextForMonitorAsync(monitor, cancellationToken).ConfigureAwait(false);
    }

    private async Task<WallpaperChangeStatus> PreviousForMonitorAsync(MonitorInfo monitor, CancellationToken cancellationToken)
    {
        WallpaperHistory history = GetHistory(monitor.Id);

        while (history.TryMoveBack(out WallpaperSelection? previous))
        {
            if (AllImagesExist(previous))
            {
                await ApplyAsync(monitor, previous, cancellationToken).ConfigureAwait(false);
                return WallpaperChangeStatus.Changed;
            }

            LogHistoryImageMissing(previous.Images);
        }

        LogNoPreviousWallpaper(monitor.DisplayName);
        return WallpaperChangeStatus.NoPreviousWallpaper;
    }

    private async Task ApplyAsync(MonitorInfo monitor, WallpaperSelection selection, CancellationToken cancellationToken)
    {
        string wallpaperPath = selection.IsSingleImage
            ? selection.Images[0]!
            : await ComposeAsync(monitor, selection, cancellationToken).ConfigureAwait(false);

        await _wallpaperService.SetWallpaperAsync(monitor.Id, wallpaperPath, cancellationToken).ConfigureAwait(false);
        LogWallpaperChanged(monitor.DisplayName, selection.Layout, selection.Images);
        WallpaperChanged?.Invoke(this, new WallpaperChangedEventArgs(monitor.Id, wallpaperPath, selection));
    }

    private Task<string> ComposeAsync(MonitorInfo monitor, WallpaperSelection selection, CancellationToken cancellationToken)
    {
        (int columns, int rows) = selection.Layout.GetGridSize();
        var request = new WallpaperCompositionRequest(
            monitor.Id, selection.Images, columns, rows, monitor.Width, monitor.Height);
        return _composer.ComposeAsync(request, cancellationToken);
    }

    private bool AllImagesExist(WallpaperSelection selection) => selection.ShownImages.All(_imageProvider.Exists);

    private WallpaperHistory GetHistory(string monitorId)
    {
        if (!_histories.TryGetValue(monitorId, out WallpaperHistory? history))
        {
            history = new WallpaperHistory();
            _histories[monitorId] = history;
        }

        return history;
    }

    private IImageSelector GetSelector(ImageSelectionMode mode) =>
        _selectors.TryGetValue(mode, out IImageSelector? selector)
            ? selector
            : throw new InvalidOperationException($"選択モード '{mode}' に対応する IImageSelector が登録されていません。");

    [LoggerMessage(Level = LogLevel.Information, Message = "壁紙を変更しました: {Monitor} ({Layout}) -> {Images}")]
    private partial void LogWallpaperChanged(string monitor, WallpaperLayout layout, IReadOnlyList<string?> images);

    [LoggerMessage(Level = LogLevel.Warning, Message = "対象のモニターが見つかりません: {MonitorId}")]
    private partial void LogNoTargetMonitor(string monitorId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "壁紙フォルダが登録されていないためスキップします: {Monitor}")]
    private partial void LogNoFolders(string monitor);

    [LoggerMessage(Level = LogLevel.Warning, Message = "壁紙フォルダに対応画像がありません: {Monitor}")]
    private partial void LogNoImages(string monitor);

    [LoggerMessage(Level = LogLevel.Information, Message = "選んだ画像が見つからないため、壁紙フォルダを列挙し直して選び直します: {Monitor}")]
    private partial void LogStaleImageList(string monitor);

    [LoggerMessage(Level = LogLevel.Information, Message = "これ以上前の壁紙はありません: {Monitor}")]
    private partial void LogNoPreviousWallpaper(string monitor);

    [LoggerMessage(Level = LogLevel.Debug, Message = "履歴の画像が見つからないためスキップします: {Images}")]
    private partial void LogHistoryImageMissing(IReadOnlyList<string?> images);

    [LoggerMessage(Level = LogLevel.Error, Message = "壁紙の変更に失敗しました: {Monitor}")]
    private partial void LogMonitorChangeFailed(Exception exception, string monitor);
}
