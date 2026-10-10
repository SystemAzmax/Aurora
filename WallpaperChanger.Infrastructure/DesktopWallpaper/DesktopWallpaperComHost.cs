using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WallpaperChanger.Infrastructure.DesktopWallpaper.Interop;

namespace WallpaperChanger.Infrastructure.DesktopWallpaper;

/// <summary>
/// IDesktopWallpaper を専用の STA スレッド上で生成・呼び出す。
/// 呼び出し元スレッド（UI / スレッドプール）のアパートメントに依存せず、COM オブジェクトのスレッド親和性を保証する。
/// エクスプローラーの再起動で COM オブジェクトが切断された場合は再生成して 1 回だけ再試行する。
/// エクスプローラーが応答せず呼び出しが <see cref="DesktopWallpaperOptions.CallTimeout"/> を超えた場合は、
/// その呼び出しを <see cref="TimeoutException"/> で失敗させ、止まった STA スレッドを見捨てて新しいスレッドで続ける。
/// </summary>
internal sealed partial class DesktopWallpaperComHost : IDisposable
{
    private const int RpcEDisconnected = unchecked((int)0x80010108);
    private const int RpcSServerUnavailable = unchecked((int)0x800706BA);
    private const int CoEObjNotConnected = unchecked((int)0x800401FD);

    private readonly Func<IDesktopWallpaper> _factory;
    private readonly TimeSpan _callTimeout;
    private readonly ILogger<DesktopWallpaperComHost> _logger;
    private readonly Lock _gate = new();
    private StaWorker _worker; // _gate の内側で差し替える
    private int _workerCount;
    private bool _disposed;

    public DesktopWallpaperComHost(IOptions<DesktopWallpaperOptions> options, ILogger<DesktopWallpaperComHost> logger)
        : this(() => (IDesktopWallpaper)new DesktopWallpaperCoClass(), options, logger)
    {
    }

    /// <summary>テストで COM オブジェクトを差し替えるためのコンストラクタ。</summary>
    internal DesktopWallpaperComHost(
        Func<IDesktopWallpaper> factory,
        IOptions<DesktopWallpaperOptions> options,
        ILogger<DesktopWallpaperComHost> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _callTimeout = options.Value.CallTimeout;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _worker = CreateWorker();
    }

    public Task<T> InvokeAsync<T>(Func<IDesktopWallpaper, T> func, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(func);
        cancellationToken.ThrowIfCancellationRequested();

        var item = new WorkItem<T>(this, func, cancellationToken);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _worker.Add(item);
        }

        return item.Task;
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
        StaWorker worker;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            worker = _worker;
        }

        worker.CompleteAdding();
        if (!worker.Join(TimeSpan.FromSeconds(5)))
        {
            LogThreadJoinTimeout();
        }
    }

    private StaWorker CreateWorker() => new(this, ++_workerCount);

    /// <summary>
    /// STA スレッドで実行が始まった呼び出しを見張り、制限時間を超えたら失敗させてスレッドを作り直す。
    /// 呼び出し元のキャンセルとは無関係に見張る（呼び出し元が待つのをやめても、止まったスレッドを放置しないため）。
    /// </summary>
    private async Task WatchAsync(WorkItem item, StaWorker worker)
    {
        try
        {
            await item.Task.WaitAsync(_callTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            OnCallTimedOut(item, worker);
        }
        catch (Exception)
        {
            // 呼び出し自体の失敗・キャンセルは呼び出し元の Task で扱う
        }
    }

    private void OnCallTimedOut(WorkItem item, StaWorker worker)
    {
        item.Fail(new TimeoutException(
            $"IDesktopWallpaper の呼び出しが {_callTimeout.TotalSeconds:0.#} 秒以内に完了しませんでした。エクスプローラーが応答していない可能性があります。"));

        int pendingCount;
        lock (_gate)
        {
            if (_disposed || !ReferenceEquals(_worker, worker))
            {
                return;
            }

            // 止まったスレッドは戻ってこない可能性があるため見捨て、待っていた呼び出しを新しいスレッドに移す
            List<WorkItem> pending = worker.Abandon();
            _worker = CreateWorker();
            foreach (WorkItem p in pending)
            {
                _worker.Add(p);
            }

            pendingCount = pending.Count;
        }

        LogCallTimedOut(_callTimeout, worker.Id, pendingCount);
    }

    private static bool IsDisconnected(int hresult) =>
        hresult is RpcEDisconnected or RpcSServerUnavailable or CoEObjNotConnected;

    [LoggerMessage(Level = LogLevel.Warning, Message = "IDesktopWallpaper が切断されたため再接続します (HRESULT: 0x{HResult:X8})")]
    private partial void LogReconnecting(int hresult);

    [LoggerMessage(Level = LogLevel.Warning, Message = "IDesktopWallpaper の STA スレッドが時間内に終了しませんでした")]
    private partial void LogThreadJoinTimeout();

    [LoggerMessage(Level = LogLevel.Error, Message = "IDesktopWallpaper の呼び出しが {Timeout} 以内に完了しないため、STA スレッド #{WorkerId} を破棄して作り直しました（待機中の呼び出し {PendingCount} 件は新しいスレッドで実行します）")]
    private partial void LogCallTimedOut(TimeSpan timeout, int workerId, int pendingCount);

    /// <summary>STA スレッド上で実行する 1 回分の呼び出し。</summary>
    private abstract class WorkItem
    {
        public abstract Task Task { get; }

        public abstract void Execute(StaWorker worker);

        public abstract void Fail(Exception exception);
    }

    private sealed class WorkItem<T>(DesktopWallpaperComHost owner, Func<IDesktopWallpaper, T> func, CancellationToken cancellationToken)
        : WorkItem
    {
        private readonly TaskCompletionSource<T> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override Task<T> Task => _tcs.Task;

        public override void Execute(StaWorker worker)
        {
            // キャンセルは実行開始時に判定する。制限時間を超えて既に失敗させた呼び出しは実行しない
            if (cancellationToken.IsCancellationRequested)
            {
                _tcs.TrySetCanceled(cancellationToken);
                return;
            }

            if (_tcs.Task.IsCompleted)
            {
                return;
            }

            _ = owner.WatchAsync(this, worker);
            try
            {
                _tcs.TrySetResult(worker.ExecuteWithReconnect(func));
            }
            catch (Exception ex)
            {
                // 例外は呼び出し元の Task へ伝播させる
                _tcs.TrySetException(ex);
            }
        }

        public override void Fail(Exception exception) => _tcs.TrySetException(exception);
    }

    /// <summary>COM オブジェクトを所有する STA スレッドと、その作業キュー。</summary>
    private sealed class StaWorker
    {
        private readonly DesktopWallpaperComHost _owner;
        private readonly BlockingCollection<WorkItem> _queue = [];
        private readonly Thread _thread;
        private IDesktopWallpaper? _instance; // このスレッドからのみアクセスする

        public StaWorker(DesktopWallpaperComHost owner, int id)
        {
            _owner = owner;
            Id = id;
            _thread = new Thread(Run)
            {
                IsBackground = true, // 見捨てたスレッドが止まったままでもプロセスは終了できる
                Name = $"DesktopWallpaper STA #{id}",
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }

        public int Id { get; }

        public void Add(WorkItem item) => _queue.Add(item);

        public void CompleteAdding() => _queue.CompleteAdding();

        public bool Join(TimeSpan timeout)
        {
            if (!_thread.Join(timeout))
            {
                return false;
            }

            _queue.Dispose();
            return true;
        }

        /// <summary>新しい作業を受け付けないようにし、まだ実行していない作業を取り出す。</summary>
        public List<WorkItem> Abandon()
        {
            _queue.CompleteAdding();

            var pending = new List<WorkItem>();
            while (_queue.TryTake(out WorkItem? item))
            {
                pending.Add(item);
            }

            return pending;
        }

        public T ExecuteWithReconnect<T>(Func<IDesktopWallpaper, T> func)
        {
            try
            {
                return func(GetInstance());
            }
            catch (COMException ex) when (IsDisconnected(ex.HResult))
            {
                _owner.LogReconnecting(ex.HResult);
                ReleaseInstance();
                return func(GetInstance());
            }
        }

        private IDesktopWallpaper GetInstance() => _instance ??= _owner._factory();

        private void ReleaseInstance()
        {
            if (_instance is not null)
            {
                if (Marshal.IsComObject(_instance))
                {
                    Marshal.FinalReleaseComObject(_instance);
                }

                _instance = null;
            }
        }

        private void Run()
        {
            try
            {
                foreach (WorkItem work in _queue.GetConsumingEnumerable())
                {
                    work.Execute(this);
                }
            }
            finally
            {
                ReleaseInstance();
            }
        }
    }
}
