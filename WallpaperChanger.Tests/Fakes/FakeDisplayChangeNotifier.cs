using WallpaperChanger.Core.Interfaces;

namespace WallpaperChanger.Tests.Fakes;

/// <summary>
/// テストから任意のタイミングでディスプレイ構成の変更通知を発生させる。
/// </summary>
internal sealed class FakeDisplayChangeNotifier : IDisplayChangeNotifier
{
    public event EventHandler? DisplaysChanged;

    public bool HasSubscribers => DisplaysChanged is not null;

    public void Raise() => DisplaysChanged?.Invoke(this, EventArgs.Empty);
}
