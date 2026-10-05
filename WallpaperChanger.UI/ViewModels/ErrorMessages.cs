namespace WallpaperChanger.UI.ViewModels;

internal static class ErrorMessages
{
    /// <summary>ユーザーに表示する例外メッセージを取り出す（AggregateException は内側を連結する）。</summary>
    public static string Describe(Exception exception) => exception switch
    {
        AggregateException aggregate => string.Join(
            Environment.NewLine,
            aggregate.Flatten().InnerExceptions.Select(e => e.Message).Distinct()),
        _ => exception.Message,
    };
}
