using WallpaperChanger.Core.Interfaces;
using WallpaperChanger.Core.Models;

namespace WallpaperChanger.Core.Services;

/// <summary>
/// 候補の中からランダムに count 枚を重複なく選ぶ。
/// 可能な限り現在表示している画像以外を選び、足りない場合のみ現在の画像や重複で補う。
/// </summary>
public sealed class RandomImageSelector : IImageSelector
{
    private readonly Random _random;

    public RandomImageSelector()
        : this(Random.Shared)
    {
    }

    /// <summary>テスト用にシード固定の乱数を差し込むためのコンストラクタ。</summary>
    internal RandomImageSelector(Random random)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
    }

    public ImageSelectionMode Mode => ImageSelectionMode.Random;

    public IReadOnlyList<string> SelectNext(IReadOnlyList<string> images, IReadOnlyList<string> currentImages, int count)
    {
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(currentImages);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        if (images.Count == 0)
        {
            return [];
        }

        var current = new HashSet<string>(currentImages, StringComparer.OrdinalIgnoreCase);
        List<string> fresh = [.. images.Where(i => !current.Contains(i))];
        List<string> shown = [.. images.Where(current.Contains)];

        // 1. 現在表示していない画像から重複なく選ぶ
        List<string> selected = TakeRandom(fresh, count);

        // 2. 足りなければ現在表示している画像から補う
        if (selected.Count < count)
        {
            selected.AddRange(TakeRandom(shown, count - selected.Count));
        }

        // 3. それでも足りない（候補が count 枚未満）場合は重複を許して補う
        while (selected.Count < count)
        {
            selected.Add(images[_random.Next(images.Count)]);
        }

        return selected;
    }

    /// <summary>部分的な Fisher–Yates シャッフルで重複なく取り出す。</summary>
    private List<string> TakeRandom(List<string> source, int count)
    {
        int take = Math.Min(count, source.Count);
        for (int i = 0; i < take; i++)
        {
            int j = _random.Next(i, source.Count);
            (source[i], source[j]) = (source[j], source[i]);
        }

        return source.GetRange(0, take);
    }
}
