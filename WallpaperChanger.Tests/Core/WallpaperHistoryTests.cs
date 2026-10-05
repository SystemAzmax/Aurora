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
}
