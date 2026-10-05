using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;
using WallpaperChanger.Core.Interfaces;
using WallpaperChanger.UI.ViewModels;

namespace WallpaperChanger.UI.Services;

/// <summary>
/// Resources/TrayIcon.xaml に定義した TaskbarIcon を表示し、TrayIconViewModel と結び付ける。
/// </summary>
internal sealed class TrayIconService(TrayIconViewModel viewModel) : ITrayIconService
{
    private const string TrayIconResourceKey = "TrayIcon";
    private const string ContextMenuResourceKey = "TrayContextMenu";

    private readonly TrayIconViewModel _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    private TaskbarIcon? _taskbarIcon;
    private bool _disposed;

    public void Show()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_taskbarIcon is not null)
        {
            return;
        }

        var icon = (TaskbarIcon)Application.Current.FindResource(TrayIconResourceKey);
        icon.DataContext = _viewModel;

        // メニューは DataContext 設定後に取り付ける。メニュー自身にも先に設定しておくことで、
        // バインディングが一度も TaskbarIcon を参照しないようにする。
        var menu = (ContextMenu)Application.Current.FindResource(ContextMenuResourceKey);
        menu.DataContext = _viewModel;
        icon.ContextMenu = menu;

        _viewModel.NotificationRequested += OnNotificationRequested;
        _taskbarIcon = icon;
    }

    public void ShowNotification(string title, string message) =>
        _taskbarIcon?.ShowBalloonTip(title, message, BalloonIcon.Info);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _viewModel.NotificationRequested -= OnNotificationRequested;
        _taskbarIcon?.Dispose();
        _taskbarIcon = null;
    }

    private void OnNotificationRequested(object? sender, TrayNotificationEventArgs e) =>
        _taskbarIcon?.ShowBalloonTip(e.Title, e.Message, e.IsError ? BalloonIcon.Error : BalloonIcon.Info);
}
