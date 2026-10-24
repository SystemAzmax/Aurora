using Microsoft.Extensions.Logging;
using WallpaperChanger.Core.Interfaces;
using WallpaperChanger.Core.Models;

namespace WallpaperChanger.Core.Services;

/// <summary>
/// 接続先のポートを変えたモニターに、それまでの設定と履歴を引き継ぐ。
/// モニターの ID（デバイスパス）には接続先ごとの識別子が含まれ、接続先を変えると別のモニターとして扱われるため、
/// ID に含まれる型番（<see cref="MonitorHardwareId"/>）が同じ、接続していないモニターの設定を探して移す。
/// </summary>
/// <remarks>
/// 設定はコピーせずに移す。コピーすると同じ型番の設定が増えていき、次に接続先を変えたときに引き継ぎ元が決まらなくなるため。
/// そのため、同じ型番のモニターを場所ごとに使い分けている場合は、設定が共通になる。
/// </remarks>
public sealed partial class MonitorSettingsInheritor(
    ISettingsService settingsService,
    IWallpaperRotationService rotationService,
    ILogger<MonitorSettingsInheritor> logger) : IMonitorSettingsInheritor
{
    private readonly ISettingsService _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    private readonly IWallpaperRotationService _rotationService = rotationService ?? throw new ArgumentNullException(nameof(rotationService));
    private readonly ILogger<MonitorSettingsInheritor> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<IReadOnlyList<MonitorInfo>> InheritAsync(
        IReadOnlyList<MonitorInfo> connectedMonitors,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connectedMonitors);

        AppSettings settings = _settingsService.Current;
        List<(MonitorSettings From, MonitorInfo To)> inheritances = FindInheritances(settings, connectedMonitors);

        AppSettings Apply(AppSettings s) => inheritances
            .Aggregate(s, (current, i) => current.MoveMonitor(i.From.MonitorId, i.To.Id))
            .RecordMonitorNames(connectedMonitors);

        // 名前も変わらなければ、設定ファイルを書き直さない
        if (inheritances.Count == 0 && Apply(settings).Monitors.SequenceEqual(settings.Monitors))
        {
            return [];
        }

        await _settingsService.UpdateAsync(Apply, cancellationToken).ConfigureAwait(false);

        foreach ((MonitorSettings from, MonitorInfo to) in inheritances)
        {
            LogInherited(to.DisplayName, from.MonitorId, to.Id);
            await _rotationService.InheritHistoryAsync(from.MonitorId, to.Id, cancellationToken).ConfigureAwait(false);
        }

        return [.. inheritances.Select(i => i.To)];
    }

    private static List<(MonitorSettings From, MonitorInfo To)> FindInheritances(
        AppSettings settings,
        IReadOnlyList<MonitorInfo> connectedMonitors)
    {
        var connectedIds = new HashSet<string>(connectedMonitors.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);

        // 引き継ぎ元: 壁紙フォルダを設定した、接続していないモニター
        ILookup<string, MonitorSettings> sources = settings.Monitors
            .Where(s => !connectedIds.Contains(s.MonitorId) && s.HasFolderSettings)
            .Select(s => (Settings: s, HardwareId: MonitorHardwareId.FromMonitorId(s.MonitorId)))
            .Where(s => s.HardwareId is not null)
            .ToLookup(s => s.HardwareId!, s => s.Settings, StringComparer.OrdinalIgnoreCase);

        // 引き継ぎ先: 壁紙フォルダを設定していない、接続中のモニター
        IEnumerable<IGrouping<string, MonitorInfo>> targets = connectedMonitors
            .Where(m => !settings.GetMonitor(m.Id).HasFolderSettings)
            .Select(m => (Monitor: m, HardwareId: MonitorHardwareId.FromMonitorId(m.Id)))
            .Where(m => m.HardwareId is not null)
            .GroupBy(m => m.HardwareId!, m => m.Monitor, StringComparer.OrdinalIgnoreCase);

        var inheritances = new List<(MonitorSettings From, MonitorInfo To)>();
        foreach (IGrouping<string, MonitorInfo> target in targets)
        {
            // 同じ型番のモニターが複数あると、どの設定がどのモニターのものか決められないため引き継がない
            MonitorSettings[] candidates = [.. sources[target.Key]];
            if (target.Count() == 1 && candidates.Length == 1)
            {
                inheritances.Add((candidates[0], target.Single()));
            }
        }

        return inheritances;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "接続先が変わったモニターに前の設定を引き継ぎました: {Monitor} ({FromMonitorId} -> {ToMonitorId})")]
    private partial void LogInherited(string monitor, string fromMonitorId, string toMonitorId);
}
