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

    /// <summary>内容または位置が変わるたびに増える番号。保存が必要かの判定に使う。</summary>
    public int Version { get; private set; }

    /// <summary>
    /// 保存した履歴から復元する。表示できない組（レイアウトと画像の枚数が合わないもの）は除き、
    /// 容量を超える分は古いものから除く。位置は除いた組の分だけずらし、範囲内に収める。
    /// </summary>
    public static WallpaperHistory Restore(WallpaperHistorySnapshot snapshot, int capacity = DefaultCapacity)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var history = new WallpaperHistory(capacity);
        IReadOnlyList<WallpaperSelection?> items = snapshot.Items ?? [];
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] is not { } selection || !IsRestorable(selection))
            {
                continue;
            }

            history._items.Add(selection);
            if (i <= snapshot.Position)
            {
                history._position = history._items.Count - 1;
            }
        }

        int excess = history._items.Count - capacity;
        if (excess > 0)
        {
            history._items.RemoveRange(0, excess);
            history._position -= excess;
        }

        if (history._items.Count > 0)
        {
            history._position = Math.Clamp(history._position, 0, history._items.Count - 1);
        }

        return history;
    }

    /// <summary>保存用に、今の内容と位置を写し取る。</summary>
    public WallpaperHistorySnapshot ToSnapshot(string monitorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(monitorId);
        return new WallpaperHistorySnapshot(monitorId, [.. _items], _position);
    }

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
        Version++;
    }

    public bool TryMoveBack([NotNullWhen(true)] out WallpaperSelection? selection)
    {
        if (_position <= 0)
        {
            selection = null;
            return false;
        }

        _position--;
        Version++;
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
        Version++;
        selection = _items[_position];
        return true;
    }

    // 手で編集されたファイルや別の版で保存したファイルでも、表示できない組で切り替えが失敗しないようにする
    private static bool IsRestorable(WallpaperSelection selection) =>
        Enum.IsDefined(selection.Layout) && selection.Images.Count == selection.Layout.GetTileCount();
}
