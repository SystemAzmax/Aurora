# Aurora Wallpaper Changer User Guide

English | [日本語](UserGuide_ja.md)

Aurora Wallpaper Changer sets images from the folders you choose for each monitor as your wallpaper, and changes them at the interval you set.
It runs in the notification area (system tray).

- [1. Getting Started](#1-getting-started)
- [2. Settings Window](#2-settings-window)
- [3. Tray Menu](#3-tray-menu)
- [4. Troubleshooting](#4-troubleshooting)
- [5. Uninstalling](#5-uninstalling)
- [Contact](#contact)

## 1. Getting Started

### Start the App

Start "Aurora Wallpaper Changer" from the Start menu.
The first time you start it, the Settings window opens because no wallpaper folders are set yet.

Closing the Settings window does not exit the app. It keeps running in the notification area and keeps changing wallpapers.
To exit the app, right-click the tray icon and choose "Exit".

### Find the Tray Icon

The icon appears at the right end of the taskbar, near the clock.
If you can't see it, select "^" (Show hidden icons) on the taskbar. The icon is there.

To keep it visible, drag the icon from "^" to the taskbar near the clock.

- Double-click the icon to open the Settings window.
- Right-click the icon to open the menu (see [3. Tray Menu](#3-tray-menu)).
- Point to the icon to see the change interval and whether changes are paused.
- If you start the app again from the Start menu, a second copy doesn't start. The Settings window opens instead.

### Set Wallpaper Folders

1. In "Monitors" on the left side of the Settings window, select the monitor to set up.
   If you're not sure which monitor is which, select "Identify" to show a number on the screen of each monitor.
2. Under "Wallpaper folders", select "Add" and choose a folder of images to use as wallpapers.
   You can add more than one folder. To also use images in folders inside it (subfolders), turn on "Include subfolders".
3. Choose a layout: Single image, 2 × 2 tiles, 4 × 4 tiles, or Span.
4. Set up the other monitors in the same way.

Supported images are JPG / JPEG / PNG / BMP / WEBP.
Settings are saved and applied to your wallpapers as soon as you change them. There is no Save button.
The bar at the bottom of the Settings window shows the result of what you did.

## 2. Settings Window

In the Settings window, you select a monitor on the left and set it up on the right.
Items on the right marked "(all monitors)" are the same no matter which monitor is selected.

### Monitors

| Item | Description |
| --- | --- |
| Identify | Shows a number on the screen of each monitor. The numbers match the list on the left |
| Detect monitors | Reloads the monitor list. Use it when a monitor you connected isn't in the list |

When you connect or disconnect monitors, the app notices and updates the list.
If you reconnect the same model to a different port, its previous settings are carried over.

### Wallpaper Folders and Layout

| Layout | Description |
| --- | --- |
| Single image | Shows one image on the whole screen |
| 2 × 2 tiles | Shows 4 images at once in a 2 × 2 grid |
| 4 × 4 tiles | Shows 16 images at once in a 4 × 4 grid |
| Span | Shows one image across multiple monitors |

#### Single Image

Use "Fit" to choose how the image fits each monitor.

| Fit | Description |
| --- | --- |
| Use the Windows setting | Sets the image as is and shows it with the fit chosen in Windows "Personalization > Background" (this Windows setting applies to all monitors) |
| Fill | Scales the image to cover the whole screen and crops the overflow around the center |
| Fit | Scales the image so that all of it fits on the screen, with black margins. Portrait photos are shown without being cut off |
| Stretch | Stretches the image to fill the screen, changing its aspect ratio |
| Center | Shows the image at its original size in the center. Images larger than the screen are cropped around the center, and margins are black |

#### 2 × 2 Tiles and 4 × 4 Tiles

- You can set folders separately for the top left, top right, bottom left, and bottom right.
  With 2 × 2 tiles, each folder setting is used for one tile. With 4 × 4 tiles, it is used for a section of 2 × 2 tiles. Both layouts share the same settings.
- If no tile has folders, images are chosen from the single-image folders.
- Once any tile has folders, tiles without folders are left blank.
- You can also choose "Fit in tiles" (Fill the tiles or Show the whole image), the tile "Spacing", the "Spacing color", and the "Blank tile and margin color".

#### Span

- One image is split across the monitors set to Span, to match the position of each monitor.
- The positions follow the monitor arrangement in Windows "Settings > System > Display". Monitor bezels are not taken into account.
- Folders, subfolder search, selection, and fit (Fill all monitors or Show the whole image) are shared by all monitors set to Span.

### Preview (Current Wallpaper)

Shows the current wallpaper of the selected monitor.
Use "Previous wallpaper" and "Next wallpaper" to change the wallpaper of that monitor only.

### Changing (All Monitors)

| Item | Description |
| --- | --- |
| Change every | How often the wallpaper changes. Enter a number and a unit (min or hr), from 1 minute to 24 hours. The default is 30 minutes |
| Don't change while a full-screen app is running | Waits during full-screen games, videos, and presentations, and changes after they end |
| Don't change on battery power | Waits while a laptop or other PC is unplugged, and changes after it is plugged in |
| Refresh all wallpapers | Reloads the wallpaper folders and picks new wallpapers for all monitors. Use it after adding images to a folder |

The two waiting options are on by default. Manual changes, such as "Next wallpaper", happen right away even when they are on.

### Selection

| Selection | Description |
| --- | --- |
| Random | Picks images from the folders at random |
| In order | Shows images one by one in file path order |

### Copy Settings from Another Monitor

Copies the wallpaper folders, layout, fit, selection, and other settings of a previous monitor (including monitors that aren't connected now)
to the selected monitor, for example when you get a new monitor.

### Hidden Images (All Monitors)

A list of images you hid with "Don't show this image" in the tray menu.
Select "Show again" to use an image as a wallpaper again, or "Open location" to open the folder that contains it.

### Startup, Language and Logs

| Item | Description |
| --- | --- |
| Start automatically with Windows | When on, the app starts when you sign in to Windows and begins changing wallpapers. Off by default |
| Language | Choose Japanese or English. Takes effect after you restart the app |
| Open log folder | Opens the folder that contains the app's activity logs. Logs are kept for 14 days |

### Export and Import Settings

Export your settings to a file to import them on another PC or after reinstalling the app.

- "Export settings" saves the current settings to a `.json` file.
- "Import settings" asks you to confirm, then replaces all current settings with the settings in the file you choose.
- "Start automatically with Windows" and the wallpaper history (images you can go back to with "Previous wallpaper") are not included.
- If your monitors are different from when you exported, settings are applied automatically to monitors of the same model.
  Settings for the other monitors can be used with "Copy settings from another monitor".

### App Information

| Item | Description |
| --- | --- |
| About this app | Shows the app version and other information |
| License information | Shows the licenses of the software this app uses |
| User guide | Opens this page (the user guide) in your web browser |

## 3. Tray Menu

Right-click the tray icon to open the following menu.

| Menu | Description |
| --- | --- |
| Next wallpaper | Changes the wallpaper on all monitors to the next image |
| Previous wallpaper | Goes back to the previous wallpaper on all monitors |
| Refresh all wallpapers | Reloads the wallpaper folders and picks new wallpapers for all monitors |
| Don't show this image | Choose a shown image to stop using it as a wallpaper and replace it with another image (you can undo this in "Hidden images" in Settings) |
| Open image location | Choose a shown image to open File Explorer with the image selected |
| Pause / Resume | Stops automatic changes. While paused, the item changes to "Resume" |
| Settings | Opens the Settings window |
| Exit | Exits the app |

For a monitor with tiles, "Don't show this image" and "Open image location" let you choose a tile first, then the image.

## 4. Troubleshooting

### Nothing Appears When I Start the App

The app runs in the notification area. Check whether the icon is under "^" on the taskbar (see [Find the Tray Icon](#find-the-tray-icon)).
Starting the app again from the Start menu opens the Settings window.

### Wallpapers Don't Change Automatically

Check the following:

- Point to the icon and check whether it says "paused". If it does, choose "Resume" in the menu.
- Check whether a full-screen app (a game, video, presentation, and so on) is running. The wallpaper changes after it ends.
- Check whether your laptop is plugged in. The app waits while running on battery power.
  To change wallpapers without waiting, turn off the two options in "Changing".
- Check that the monitor has wallpaper folders and that the folders contain supported images (JPG / JPEG / PNG / BMP / WEBP).

### WEBP Images Aren't Shown

Reading WEBP images requires the WebP support in Windows.
Windows 11 includes it. On Windows 10, install "WebP Image Extensions" from the Microsoft Store.

### I Can't Turn On "Start automatically with Windows"

If you turned off startup for the app in Windows Settings or Task Manager, you can't turn it on from the app.
Turn on "Aurora Wallpaper Changer" in Windows "Settings > Apps > Startup".
If an administrator has blocked startup apps (for example, on a work PC), you can't turn it on.

### I Replaced or Reconnected a Monitor

If you reconnect the same model to a different port, its settings are carried over automatically.
For a new monitor, use "Copy settings from another monitor" to copy the settings of the previous monitor.

### I Want to Move My Settings to Another PC

Use [Export and Import Settings](#export-and-import-settings).

## 5. Uninstalling

In the Start menu, right-click "Aurora Wallpaper Changer" and choose "Uninstall".

- Information the app saved on your PC, such as settings, wallpaper history, and logs, is also deleted.
  To keep your settings for later, save them with "Export settings" before uninstalling.
- Your wallpaper does not go back to the original one. Set it again in Windows "Settings > Personalization > Background".

## Contact

To report a bug or make a request, please use [GitHub Issues](https://github.com/SystemAzmax/Aurora/issues).
For privacy, see the [Privacy Policy](PrivacyPolicy.md).
