using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using WallpaperChanger.Infrastructure.DesktopWallpaper.Interop;

namespace WallpaperChanger.Infrastructure.DesktopWallpaper;

/// <summary>
/// IDesktopWallpaper を専用の STA スレッド上で生成・呼び出す。
/// 呼び出し元スレッド（UI / スレッドプール）のアパートメントに依存せず、COM オブジェクトのスレッド親和性を保証する。
/// エクスプローラーの再起動で COM オブジェクトが切断された場合は再生成して 1 回だけ再試行する。
/// </summary>
internal sealed partial class DesktopWallpaperComHost : IDisposable
{
    private const int RpcEDisconnected = unchecked((int)0x80010108);
    private const int RpcSServerUnavailable = unchecked((int)0x800706BA);
    private const int CoEObjNotConnected = unchecked((int)0x800401FD);

    private readonly BlockingCollection<Action> _queue = [];
    private readonly Thread _thread;
    private readonly ILogger<DesktopWallpaperComHost> _logger;
    private IDesktopWallpaper? _instance; // STA スレッドからのみアクセスする
    private bool _disposed;

    public DesktopWallpaperComHost(ILogger<DesktopWallpaperComHost> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "DesktopWallpaper STA",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public Task<T> InvokeAsync<T>(Func<IDesktopWallpaper, T> func, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(func);
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(() =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                tcs.TrySetCanceled(cancellationToken);
                return;
            }

            try
            {
                tcs.TrySetResult(ExecuteWithReconnect(func));
            }
            catch (Exception ex)
            {
                // 例外は呼び出し元の Task へ伝播させる
                tcs.TrySetException(ex);
            }
        }, CancellationToken.None); // キャンセルは作業項目の実行時に判定する

        return tcs.Task;
    }

    public Task InvokeAsync(Action<IDesktopWallpaper> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        return InvokeAsync<object?>(
            wallpaper =>
            {
                action(wallpaper);
                return null;
            },
            cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _queue.CompleteAdding();
        if (!_thread.Join(TimeSpan.FromSeconds(5)))
        {
            LogThreadJoinTimeout();
            return;
        }

        _queue.Dispose();
    }

    private T ExecuteWithReconnect<T>(Func<IDesktopWallpaper, T> func)
    {
        try
        {
            return func(GetInstance());
        }
        catch (COMException ex) when (IsDisconnected(ex.HResult))
        {
            LogReconnecting(ex.HResult);
            ReleaseInstance();
            return func(GetInstance());
        }
    }

    private IDesktopWallpaper GetInstance() =>
        _instance ??= (IDesktopWallpaper)new DesktopWallpaperCoClass();

    private void ReleaseInstance()
    {
        if (_instance is not null)
        {
            Marshal.FinalReleaseComObject(_instance);
            _instance = null;
        }
    }

    private void Run()
    {
        try
        {
            foreach (Action work in _queue.GetConsumingEnumerable())
            {
                work();
            }
        }
        finally
        {
            ReleaseInstance();
        }
    }

    private static bool IsDisconnected(int hresult) =>
        hresult is RpcEDisconnected or RpcSServerUnavailable or CoEObjNotConnected;

    [LoggerMessage(Level = LogLevel.Warning, Message = "IDesktopWallpaper が切断されたため再接続します (HRESULT: 0x{HResult:X8})")]
    private partial void LogReconnecting(int hresult);

    [LoggerMessage(Level = LogLevel.Warning, Message = "IDesktopWallpaper の STA スレッドが時間内に終了しませんでした")]
    private partial void LogThreadJoinTimeout();
}
