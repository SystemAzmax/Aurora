using Microsoft.Extensions.Options;
using WallpaperChanger.Core.Interfaces;

namespace WallpaperChanger.Infrastructure.Logging;

internal sealed class FileLogDirectoryProvider(IOptions<FileLoggerOptions> options) : ILogDirectoryProvider
{
    public string LogDirectory { get; } = Path.GetFullPath((options ?? throw new ArgumentNullException(nameof(options))).Value.Directory);
}
