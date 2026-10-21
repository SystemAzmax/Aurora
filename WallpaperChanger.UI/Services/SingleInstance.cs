using WallpaperChanger.UI.Interop;

namespace WallpaperChanger.UI.Services;

/// <summary>
/// 多重起動を防ぐ。2 つ目に起動されたプロセスは、起動中のプロセスへ前面に出るよう依頼して終了する。
/// 範囲はサインイン中のユーザーのセッションごと（壁紙はユーザーごとの設定のため）。
/// </summary>
internal sealed class SingleInstance : IActivationRequestListener, IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activationRequest;
    private readonly Lock _gate = new();
    private RegisteredWaitHandle? _registration;
    private bool _disposed;

    private SingleInstance(Mutex mutex, EventWaitHandle activationRequest)
    {
        _mutex = mutex;
        _activationRequest = activationRequest;
    }

    /// <summary>
    /// 最初のプロセスなら、破棄するまで多重起動を防ぐ <see cref="SingleInstance"/> を返す。
    /// 既に起動中なら、そのプロセスへ前面に出るよう依頼して null を返す。
    /// </summary>
    public static SingleInstance? TryAcquire(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        // Mutex より先に作る。Mutex があれば、その持ち主が作った要求用のイベントもあるため、依頼が必ず届く
        var activationRequest = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{name}.Activate");
        var mutex = new Mutex(initiallyOwned: true, $@"Local\{name}", out bool createdNew);
        if (createdNew)
        {
            return new SingleInstance(mutex, activationRequest);
        }

        // 起動中のプロセスは裏にいるため、そのままでは画面を前面に出せない。起動されたばかりのこちらが持つ権利を譲る
        // （譲れなくても依頼は届き、設定画面はタスクバーのボタンの点滅で知らせる）
        _ = NativeMethods.AllowSetForegroundWindow(NativeMethods.AsfwAny);
        activationRequest.Set();
        mutex.Dispose();
        activationRequest.Dispose();
        return null;
    }

    public void StartListening(Action onRequested)
    {
        ArgumentNullException.ThrowIfNull(onRequested);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_registration is not null)
            {
                throw new InvalidOperationException("既に要求の受け付けを始めています。");
            }

            // 自動リセットのイベントのため、届いた要求 1 回につき 1 回通知される（受け付け前の要求はシグナル状態のまま残っている）
            _registration = ThreadPool.RegisterWaitForSingleObject(
                _activationRequest, (_, _) => onRequested(), null, Timeout.Infinite, executeOnlyOnce: false);
        }
    }

    public void StopListening()
    {
        lock (_gate)
        {
            _registration?.Unregister(null);
            _registration = null;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _registration?.Unregister(null);
            _registration = null;
        }

        _activationRequest.Dispose();
        _mutex.Dispose();
    }
}
