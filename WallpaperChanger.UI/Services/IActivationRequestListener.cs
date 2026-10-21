namespace WallpaperChanger.UI.Services;

/// <summary>
/// 2 つ目に起動されたプロセスからの「起動中のほうを前面に出す」要求を受け取る。
/// </summary>
public interface IActivationRequestListener
{
    /// <summary>
    /// 要求の受け付けを始める。受け付ける前に届いた要求は、始めた直後に通知する。
    /// onRequested は UI スレッド以外から呼ばれる。
    /// </summary>
    void StartListening(Action onRequested);

    /// <summary>要求の受け付けを止める。</summary>
    void StopListening();
}
