using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using WallpaperChanger.Core.Interfaces;

namespace WallpaperChanger.Infrastructure.Logging;

public static class FileLoggingBuilderExtensions
{
    /// <summary>
    /// ログを %LOCALAPPDATA%\WallpaperChanger\logs に日付ごとのファイルで保存する。
    /// </summary>
    public static ILoggingBuilder AddWallpaperChangerFile(this ILoggingBuilder builder, Action<FileLoggerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = builder.Services.AddOptions<FileLoggerOptions>();
        if (configure is not null)
        {
            options.Configure(configure);
        }

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, FileLoggerProvider>());
        builder.Services.TryAddTransient<ILogDirectoryProvider, FileLogDirectoryProvider>();
        return builder;
    }
}
