using WallpaperChanger.Core.Services;

namespace WallpaperChanger.Tests.Core;

public class ImageSelectorTests
{
    private static readonly string[] Images = [@"C:\w\1.jpg", @"C:\w\2.png", @"C:\w\3.webp"];
    private static readonly string[] SixImages = [.. Enumerable.Range(1, 6).Select(i => $@"C:\w\{i}.jpg")];

    [Theory]
    [InlineData(null, @"C:\w\1.jpg")]
    [InlineData(@"C:\w\1.jpg", @"C:\w\2.png")]
    [InlineData(@"c:\W\2.PNG", @"C:\w\3.webp")]
    [InlineData(@"C:\w\3.webp", @"C:\w\1.jpg")]
    [InlineData(@"C:\other\x.jpg", @"C:\w\1.jpg")]
    public void 順送りは現在の次を選び末尾の次は先頭に戻る(string? current, string expected)
    {
        var selector = new SequentialImageSelector();

        Assert.Equal([expected], selector.SelectNext(Images, current is null ? [] : [current], 1));
    }

    [Fact]
    public void 順送りの4枚は現在の組の最後の次から連続して選ぶ()
    {
        var selector = new SequentialImageSelector();

        Assert.Equal(SixImages[..4], selector.SelectNext(SixImages, [], 4));
        Assert.Equal([SixImages[4], SixImages[5], SixImages[0], SixImages[1]],
            selector.SelectNext(SixImages, SixImages[..4], 4));
    }

    [Fact]
    public void 候補が必要枚数より少なければ繰り返して埋める()
    {
        Assert.Equal([Images[0], Images[1], Images[2], Images[0]], new SequentialImageSelector().SelectNext(Images, [], 4));

        var random = new RandomImageSelector(new Random(1)).SelectNext(Images, [], 4);
        Assert.Equal(4, random.Count);
        Assert.Equal(Images.Length, random.Distinct().Count());
    }

    [Fact]
    public void 候補が空なら空を返す()
    {
        Assert.Empty(new SequentialImageSelector().SelectNext([], [], 4));
        Assert.Empty(new RandomImageSelector().SelectNext([], [], 1));
    }

    [Fact]
    public void ランダムは候補が複数あれば現在の画像を選ばない()
    {
        var selector = new RandomImageSelector(new Random(1234));

        for (int i = 0; i < 200; i++)
        {
            string next = Assert.Single(selector.SelectNext(Images, [Images[1]], 1));
            Assert.NotEqual(Images[1], next);
            Assert.Contains(next, Images);
        }
    }

    [Fact]
    public void ランダムの4枚は重複せず可能な限り現在の組以外から選ぶ()
    {
        var selector = new RandomImageSelector(new Random(7));
        string[] current = SixImages[..4];

        for (int i = 0; i < 100; i++)
        {
            IReadOnlyList<string> next = selector.SelectNext(SixImages, current, 4);

            Assert.Equal(4, next.Distinct().Count());
            // 現在の組以外は 2 枚しかないので、その 2 枚は必ず含まれる
            Assert.Contains(SixImages[4], next);
            Assert.Contains(SixImages[5], next);
        }
    }

    [Fact]
    public void ランダムは全候補を選びうる()
    {
        var selector = new RandomImageSelector(new Random(42));

        var selected = Enumerable.Range(0, 200).Select(_ => selector.SelectNext(Images, [], 1)[0]).ToHashSet();

        Assert.Equal(Images.Length, selected.Count);
    }

    [Fact]
    public void ランダムは候補が1件ならその画像を返す()
    {
        Assert.Equal(["only.jpg"], new RandomImageSelector().SelectNext(["only.jpg"], ["only.jpg"], 1));
    }
}
