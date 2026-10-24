using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    private const string ProgressPopupResourceKey = "TrayProgressPopup";

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
        menu.Opened += OnContextMenuOpened;
        icon.ContextMenu = menu;

        _viewModel.NotificationRequested += OnNotificationRequested;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
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
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        if (_taskbarIcon?.ContextMenu is { } menu)
        {
            menu.Opened -= OnContextMenuOpened;
        }

        _taskbarIcon?.Dispose();
        _taskbarIcon = null;
    }

    /// <summary>表示中の画像は自動切り替えでも変わるため、メニューを開くたびに一覧を作り直す。</summary>
    private void OnContextMenuOpened(object sender, RoutedEventArgs e) => _viewModel.RefreshShownImagesCommand.Execute(null);

    private void OnNotificationRequested(object? sender, TrayNotificationEventArgs e) =>
        _taskbarIcon?.ShowBalloonTip(e.Title, e.Message, e.IsError ? BalloonIcon.Error : BalloonIcon.Info);

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TrayIconViewModel.IsProgressPopupOpen) || _taskbarIcon is not { } icon)
        {
            return;
        }

        if (!_viewModel.IsProgressPopupOpen)
        {
            icon.CloseBalloon();
            return;
        }

        // 表示するたびに新しく作る（リソースは x:Shared="False"）
        var popup = (FrameworkElement)Application.Current.FindResource(ProgressPopupResourceKey);
        popup.DataContext = _viewModel;
        icon.ShowCustomBalloon(popup, PopupAnimation.Fade, timeout: null);
    }
}
