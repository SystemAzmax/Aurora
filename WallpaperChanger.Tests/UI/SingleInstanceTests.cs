using WallpaperChanger.UI.Services;

namespace WallpaperChanger.Tests.UI;

public sealed class SingleInstanceTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    // 実行中のアプリやほかのテストと衝突しないよう、テストごとに別の名前を使う
    private readonly string _name = $"WallpaperChanger.Tests.SingleInstance.{Guid.NewGuid():N}";

    [Fact]
    public void 起動中なら二つ目は取得できない()
    {
        using SingleInstance? first = SingleInstance.TryAcquire(_name);

        Assert.NotNull(first);
        Assert.Null(SingleInstance.TryAcquire(_name));
    }

    [Fact]
    public void 起動中のほうを破棄すれば次は取得できる()
    {
        SingleInstance.TryAcquire(_name)!.Dispose();

        using SingleInstance? next = SingleInstance.TryAcquire(_name);

        Assert.NotNull(next);
    }

    [Fact]
    public async Task 二つ目の起動で起動中のほうへ要求が届く()
    {
        using SingleInstance first = SingleInstance.TryAcquire(_name)!;
        TaskCompletionSource requested = NewSignal();
        first.StartListening(() => requested.TrySetResult());

        Assert.Null(SingleInstance.TryAcquire(_name));

        await requested.Task.WaitAsync(WaitTimeout, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task 受け付けを始める前に届いた要求は始めたときに通知する()
    {
        using SingleInstance first = SingleInstance.TryAcquire(_name)!;
        Assert.Null(SingleInstance.TryAcquire(_name));

        TaskCompletionSource requested = NewSignal();
        first.StartListening(() => requested.TrySetResult());

        await requested.Task.WaitAsync(WaitTimeout, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task 受け付けを止めた後の要求は通知しない()
    {
        using SingleInstance first = SingleInstance.TryAcquire(_name)!;
        int count = 0;
        first.StartListening(() => Interlocked.Increment(ref count));
        first.StopListening();

        Assert.Null(SingleInstance.TryAcquire(_name));
        await Task.Delay(200, TestContext.Current.CancellationToken);

        Assert.Equal(0, Volatile.Read(ref count));
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
