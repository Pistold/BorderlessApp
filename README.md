# Borderless Window Manager

A small Windows tray app: pick a running game or any window once, and it stays borderless
automatically from then on. even if you launch the app before the game.  This only works if a game is in windowed mode before you add it to the list.

## How it works

- **GameProfile.cs** — the saved data for one game (just its process name + display name)
- **ConfigManager.cs** — saves/loads your list of games as JSON to `%AppData%\BorderlessGameApp\config.json`
- **WindowHelper.cs** — the actual WinAPI calls that strip a window's title bar/border and resize it to fill the screen
- **MainForm.cs** — the UI: list of saved games, "Add Running Game" picker, tray icon, and a background timer that checks every 1.5s for any saved game that's running and borders it
- **Program.cs** — entry point

## Using it

1. Launch a game in **windowed mode** (not fullscreen).
2. Open Borderless Window Manager, click **Add Running Game...**, select it from the list.
3. It goes borderless immediately, and is now saved.
4. From now on, whenever that game process is detected running (even if you opened this app first), it'll automatically be made borderless within ~1.5 seconds.
5. Closing the main window just minimizes it to the system tray so the watcher keeps running — use the tray icon's right-click menu to fully **Exit**.
6. Check **Start with Windows** if you want it running automatically at login (so you never have to remember to launch it first).
7. Clicking **Remove Selected** restores that game's title bar and original size/position immediately (if it's currently running), then forgets about it — it won't be touched again unless you re-add it. This works even across app restarts, since the original size/position is saved to `config.json`, not just kept in memory.
8. Only one instance can run at a time — launching a second copy while one is already running (even minimized to the tray) shows a message instead of starting a duplicate watcher.

## Known limitations / things to improve next

- Matching is by **process name**, not window title, since titles change (level names, FPS counters, etc). If two different games share an executable name (rare), they'd conflict.
- Games using **exclusive fullscreen** internally will fight this app. Make sure the game itself is set to "Windowed" in its own graphics settings first.
- The tray icon uses a placeholder system icon — swap in a real `.ico` file for a polished look.
- If you checked **Start with Windows** inside the app, that adds its own `HKCU...\Run` registry entry independently of the installer. Uninstalling via **Settings > Apps** removes the app itself, but won't know to clean up that entry — uncheck the box in-app before uninstalling to avoid a stale startup entry.
