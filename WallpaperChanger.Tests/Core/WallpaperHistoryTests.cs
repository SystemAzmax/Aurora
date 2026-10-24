using WallpaperChanger.Core.Models;
using WallpaperChanger.Core.Services;

namespace WallpaperChanger.Tests.Core;

public class WallpaperHistoryTests
{
    private static WallpaperSelection S(string path) => WallpaperSelection.ForSingleImage(path);

    [Fact]
    public void 戻って進むと元の位置に戻る()
    {
        var history = new WallpaperHistory();
        history.Push(S("a"));
        history.Push(S("b"));
        history.Push(S("c"));

        Assert.True(history.TryMoveBack(out WallpaperSelection? back1));
        Assert.Equal("b", back1.Images[0]);
        Assert.True(history.TryMoveBack(out WallpaperSelection? back2));
        Assert.Equal("a", back2.Images[0]);
        Assert.False(history.TryMoveBack(out _));

        Assert.Equal("b", history.Forward?.Images[0]);
        Assert.True(history.TryMoveForward(out WallpaperSelection? forward));
        Assert.Equal("b", forward.Images[0]);
    }

    [Fact]
    public void 戻った後にPushすると先の履歴は破棄される()
    {
        var history = new WallpaperHistory();
        history.Push(S("a"));
        history.Push(S("b"));
        history.TryMoveBack(out _);

        history.Push(S("c"));

        Assert.Equal("c", history.Current?.Images[0]);
        Assert.Null(history.Forward);
        Assert.True(history.TryMoveBack(out WallpaperSelection? back));
        Assert.Equal("a", back.Images[0]);
    }

    [Fact]
    public void 同じ内容を連続でPushしても重複しない()
    {
        var history = new WallpaperHistory();
        history.Push(new WallpaperSelection(WallpaperLayout.Grid2x2, ["a", "b", "c", "d"]));
        history.Push(new WallpaperSelection(WallpaperLayout.Grid2x2, ["A", "B", "C", "D"]));

        Assert.Equal(1, history.Count);
    }

    [Fact]
    public void レイアウトが異なれば別の履歴として記録する()
    {
        var history = new WallpaperHistory();
        history.Push(new WallpaperSelection(WallpaperLayout.SingleImage, ["a"]));
        history.Push(new WallpaperSelection(WallpaperLayout.Grid2x2, ["a"]));

        Assert.Equal(2, history.Count);
    }

    [Fact]
    public void 容量を超えると古い履歴から削除される()
    {
        var history = new WallpaperHistory(capacity: 2);
        history.Push(S("a"));
        history.Push(S("b"));
        history.Push(S("c"));

        Assert.Equal(2, history.Count);
        Assert.True(history.TryMoveBack(out WallpaperSelection? back));
        Assert.Equal("b", back.Images[0]);
        Assert.False(history.TryMoveBack(out _));
    }

    [Fact]
    public void 変更するたびに番号が増える()
    {
        var history = new WallpaperHistory();
        history.Push(S("a"));
        history.Push(S("a")); // 同じ内容は記録しない
        history.TryMoveBack(out _); // 戻れない
        Assert.Equal(1, history.Version);

        history.Push(S("b"));
        history.TryMoveBack(out _);
        history.TryMoveForward(out _);

        Assert.Equal(4, history.Version);
    }

    [Fact]
    public void 保存した内容から位置も含めて復元できる()
    {
        var history = new WallpaperHistory();
        history.Push(S("a"));
        history.Push(S("b"));
        history.Push(S("c"));
        history.TryMoveBack(out _);

        WallpaperHistorySnapshot snapshot = history.ToSnapshot("M");
        WallpaperHistory restored = WallpaperHistory.Restore(snapshot);

        Assert.Equal("M", snapshot.MonitorId);
        Assert.Equal(3, restored.Count);
        Assert.Equal("b", restored.Current?.Images[0]);
        Assert.Equal("c", restored.Forward?.Images[0]);
        Assert.True(restored.TryMoveBack(out WallpaperSelection? back));
        Assert.Equal("a", back.Images[0]);
    }

    [Fact]
    public void 保存した内容はその後の変更の影響を受けない()
    {
        var history = new WallpaperHistory();
        history.Push(S("a"));

        WallpaperHistorySnapshot snapshot = history.ToSnapshot("M");
        history.Push(S("b"));

        Assert.Single(snapshot.Items);
    }

    [Fact]
    public void 復元時に表示できない組は除き位置をずらす()
    {
        var snapshot = new WallpaperHistorySnapshot(
            "M",
            [
                S("a"),
                new WallpaperSelection(WallpaperLayout.Grid2x2, ["x"]), // 枚数がレイアウトと合わない
                null!,
                new WallpaperSelection((WallpaperLayout)99, ["y"]), // 未知のレイアウト
                S("c"),
                S("d"),
            ],
            Position: 4);

        WallpaperHistory restored = WallpaperHistory.Restore(snapshot);

        Assert.Equal(3, restored.Count);
        Assert.Equal("c", restored.Current?.Images[0]);
        Assert.Equal("d", restored.Forward?.Images[0]);
    }

    [Fact]
    public void 復元時に容量を超える分は古い組から除く()
    {
        var snapshot = new WallpaperHistorySnapshot("M", [S("a"), S("b"), S("c")], Position: 2);

        WallpaperHistory restored = WallpaperHistory.Restore(snapshot, capacity: 2);

        Assert.Equal(2, restored.Count);
        Assert.Equal("c", restored.Current?.Images[0]);
        Assert.True(restored.TryMoveBack(out WallpaperSelection? back));
        Assert.Equal("b", back.Images[0]);
        Assert.False(restored.TryMoveBack(out _));
    }

    [Theory]
    [InlineData(-1, "a")]
    [InlineData(10, "b")]
    public void 復元時に位置が範囲外なら範囲内に収める(int position, string expected)
    {
        var snapshot = new WallpaperHistorySnapshot("M", [S("a"), S("b")], position);

        WallpaperHistory restored = WallpaperHistory.Restore(snapshot);

        Assert.Equal(expected, restored.Current?.Images[0]);
    }

    [Fact]
    public void 空の内容から復元すると空の履歴になる()
    {
        WallpaperHistory restored = WallpaperHistory.Restore(new WallpaperHistorySnapshot("M", [], Position: -1));

        Assert.True(restored.IsEmpty);
        Assert.Null(restored.Current);
    }
}
