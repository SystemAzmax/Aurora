using System.Windows;
using Microsoft.Win32;

namespace WallpaperChanger.UI.Services;

internal sealed class FolderPickerService : IFolderPickerService
{
    public IReadOnlyList<string> PickFolders(string title)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
            Multiselect = true,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        };

        Window? owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        bool? result = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);

        return result == true ? dialog.FolderNames : [];
    }
}
