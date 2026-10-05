using System.Runtime.InteropServices;

namespace WallpaperChanger.UI.Interop;

internal static class NativeMethods
{
    public static readonly IntPtr HwndTopmost = new(-1);

    public const uint SwpNoSize = 0x0001;
    public const uint SwpNoActivate = 0x0010;

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
}
