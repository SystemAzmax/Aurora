using System.Windows;
using Microsoft.Win32;
using WallpaperChanger.UI.Resources;

namespace WallpaperChanger.UI.Services;

internal sealed class SettingsFileDialogService : ISettingsFileDialogService
{
    private const string DefaultExtension = ".json";

    public string? PickExportPath(string defaultFileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = Strings.ExportSettings,
            Filter = Strings.SettingsFileFilter,
            DefaultExt = DefaultExtension,
            AddExtension = true,
            FileName = defaultFileName,
            OverwritePrompt = true,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };

        return Show(dialog) ? dialog.FileName : null;
    }

    public string? PickImportPath()
    {
        var dialog = new OpenFileDialog
        {
            Title = Strings.ImportSettings,
            Filter = Strings.SettingsFileFilter,
            DefaultExt = DefaultExtension,
            CheckFileExists = true,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };

        return Show(dialog) ? dialog.FileName : null;
    }

    private static bool Show(CommonDialog dialog)
    {
        Window? owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        bool? result = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        return result == true;
    }
}
