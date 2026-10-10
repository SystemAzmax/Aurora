using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using WallpaperChanger.Infrastructure.DesktopWallpaper;
using WallpaperChanger.Infrastructure.DesktopWallpaper.Interop;

namespace WallpaperChanger.Tests.Infrastructure;

/// <summary>
/// 実機の COM オブジェクトの代わりにフェイクを使い、STA スレッドでの実行とタイムアウト時の作り直しを検証する。
/// </summary>
public sealed class DesktopWallpaperComHostTests : IDisposable
{
    private readonly ManualResetEventSlim _release = new();
    private int _createdCount;

    public void Dispose()
    {
        // 止めた呼び出しを解放し、見捨てた STA スレッドも終了させる
        _release.Set();
        _release.Dispose();
    }

    [Fact]
    public async Task 呼び出しはSTAスレッドで実行される()
    {
        using DesktopWallpaperComHost sut = CreateSut(TimeSpan.FromSeconds(10));

        ApartmentState state = await sut.InvokeAsync(_ => Thread.CurrentThread.GetApartmentState(), TestContext.Current.CancellationToken);

        Assert.Equal(ApartmentState.STA, state);
    }

    [Fact]
    public async Task 応答しない呼び出しはタイムアウトで失敗し待機中の呼び出しは新しいスレッドで実行される()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using DesktopWallpaperComHost sut = CreateSut(TimeSpan.FromMilliseconds(300));
        int hungThreadId = 0;

        Task<int> hung = sut.InvokeAsync(_ =>
        {
            hungThreadId = Environment.CurrentManagedThreadId;
            _release.Wait(); // エクスプローラーが応答しない状態
            return 0;
        }, ct);
        Task<int> queued = sut.InvokeAsync(_ => Environment.CurrentManagedThreadId, ct);

        var ex = await Assert.ThrowsAsync<TimeoutException>(() => hung.WaitAsync(TimeSpan.FromSeconds(10), ct));
        Assert.Contains("エクスプローラー", ex.Message, StringComparison.Ordinal);

        int queuedThreadId = await queued.WaitAsync(TimeSpan.FromSeconds(10), ct);
        Assert.NotEqual(hungThreadId, queuedThreadId);
        Assert.Equal(2, _createdCount); // 新しいスレッドで COM オブジェクトを作り直した

        // 作り直した後の呼び出しも新しいスレッドで実行できる
        Assert.Equal(queuedThreadId, await sut.InvokeAsync(_ => Environment.CurrentManagedThreadId, ct));
    }

    [Fact]
    public async Task 制限時間の計測は実行開始からで待機中の時間は含めない()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using DesktopWallpaperComHost sut = CreateSut(TimeSpan.FromSeconds(1));

        // それぞれ制限時間内に終わるが、2 件目は待ち時間を含めると制限時間を超える
        Task first = sut.InvokeAsync(_ => Thread.Sleep(700), ct);
        Task second = sut.InvokeAsync(_ => Thread.Sleep(700), ct);

        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10), ct);
        Assert.Equal(1, _createdCount);
    }

    [Fact]
    public async Task 切断された場合は作り直して1回だけ再試行する()
    {
        using DesktopWallpaperComHost sut = CreateSut(TimeSpan.FromSeconds(10));
        int calls = 0;

        int result = await sut.InvokeAsync(_ =>
        {
            if (++calls == 1)
            {
                throw CreateComException(unchecked((int)0x80010108)); // RPC_E_DISCONNECTED
            }

            return calls;
        }, TestContext.Current.CancellationToken);

        Assert.Equal(2, result);
        Assert.Equal(2, _createdCount);
    }

    [Fact]
    public async Task 呼び出しの例外は呼び出し元に伝わる()
    {
        using DesktopWallpaperComHost sut = CreateSut(TimeSpan.FromSeconds(10));

        await Assert.ThrowsAsync<COMException>(() => sut.InvokeAsync(
            _ => throw CreateComException(unchecked((int)0x80004005)), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void 破棄後の呼び出しは例外になる()
    {
        DesktopWallpaperComHost sut = CreateSut(TimeSpan.FromSeconds(10));
        sut.Dispose();

        Assert.Throws<ObjectDisposedException>(() => { _ = sut.InvokeAsync(_ => 0, TestContext.Current.CancellationToken); });
    }

#pragma warning disable CA2201 // COM の呼び出しが返す例外を再現するため、ランタイム予約の例外型を直接生成する
    private static COMException CreateComException(int hresult) => new($"HRESULT 0x{hresult:X8}", hresult);
#pragma warning restore CA2201

    private DesktopWallpaperComHost CreateSut(TimeSpan callTimeout) => new(
        () =>
        {
            Interlocked.Increment(ref _createdCount);
            return Substitute.For<IDesktopWallpaper>();
        },
        Options.Create(new DesktopWallpaperOptions { CallTimeout = callTimeout }),
        NullLogger<DesktopWallpaperComHost>.Instance);
}
