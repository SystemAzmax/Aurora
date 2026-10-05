using System.Diagnostics.CodeAnalysis;
using WallpaperChanger.Core.Models;

namespace WallpaperChanger.Core.Services;

/// <summary>
/// 1 モニター分の壁紙履歴（1 件 = 1 回の切り替えで表示した画像の組）。ブラウザの「戻る／進む」と同じ考え方で位置を管理する。
/// スレッドセーフではないため、呼び出し側で排他すること。
/// </summary>
internal sealed class WallpaperHistory
{
    public const int DefaultCapacity = 100;

    private readonly List<WallpaperSelection> _items = [];
    private readonly int _capacity;
    private int _position = -1;

    public WallpaperHistory(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
    }

    public WallpaperSelection? Current => _position >= 0 ? _items[_position] : null;

    /// <summary>「進む」で表示される組。無ければ null。</summary>
    public WallpaperSelection? Forward => _position < _items.Count - 1 ? _items[_position + 1] : null;

    public bool IsEmpty => _items.Count == 0;

    public int Count => _items.Count;

    /// <summary>新しく表示した組を記録する。現在位置より先の履歴は破棄される。</summary>
    public void Push(WallpaperSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

        if (selection.HasSameContent(Current))
        {
            return;
        }

        int forwardCount = _items.Count - _position - 1;
        if (forwardCount > 0)
        {
            _items.RemoveRange(_position + 1, forwardCount);
        }

        _items.Add(selection);
        if (_items.Count > _capacity)
        {
            _items.RemoveAt(0);
        }

        _position = _items.Count - 1;
    }

    public bool TryMoveBack([NotNullWhen(true)] out WallpaperSelection? selection)
    {
        if (_position <= 0)
        {
            selection = null;
            return false;
        }

        _position--;
        selection = _items[_position];
        return true;
    }

    public bool TryMoveForward([NotNullWhen(true)] out WallpaperSelection? selection)
    {
        if (_position >= _items.Count - 1)
        {
            selection = null;
            return false;
        }

        _position++;
        selection = _items[_position];
        return true;
    }
}
