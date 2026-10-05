using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using WallpaperChanger.UI.ViewModels;
using WallpaperChanger.UI.Views;

namespace WallpaperChanger.UI.Services;

/// <summary>
/// 設定画面ごとに DI スコープを作成し、画面を閉じたときに ViewModel ごと破棄する。
/// </summary>
internal sealed class SettingsWindowService(IServiceScopeFactory scopeFactory) : ISettingsWindowService
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    private SettingsWindow? _window;
    private IServiceScope? _scope;

    public void Show()
    {
        if (_window is not null)
        {
            if (_window.WindowState == WindowState.Minimized)
            {
                _window.WindowState = WindowState.Normal;
            }

            _window.Activate();
            return;
        }

        IServiceScope scope = _scopeFactory.CreateScope();
        var viewModel = scope.ServiceProvider.GetRequiredService<SettingsViewModel>();

        // コードビハインドを持たないため、生成された InitializeComponent をここで呼び出す
        var window = new SettingsWindow();
        window.InitializeComponent();
        window.DataContext = viewModel;
        window.Closed += OnWindowClosed;

        _scope = scope;
        _window = window;

        window.Show();
        window.Activate();
        viewModel.InitializeCommand.Execute(null);
    }

    public void Close() => _window?.Close();

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (_window is not null)
        {
            _window.Closed -= OnWindowClosed;
            _window.DataContext = null;
        }

        _scope?.Dispose();
        _scope = null;
        _window = null;
    }
}
