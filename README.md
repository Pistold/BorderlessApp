# Borderless Window Manager

A small Windows tray app: pick a running game once, and it stays borderless
automatically from then on — even if you launch the app before the game.

## How it works

- **GameProfile.cs** — the saved data for one game (just its process name + display name)
- **ConfigManager.cs** — saves/loads your list of games as JSON to `%AppData%\BorderlessGameApp\config.json`
- **WindowHelper.cs** — the actual WinAPI calls that strip a window's title bar/border and resize it to fill the screen
- **MainForm.cs** — the UI: list of saved games, "Add Running Game" picker, tray icon, and a background timer that checks every 1.5s for any saved game that's running and borders it
- **Program.cs** — entry point

## Building it

1. Install **Visual Studio Community** (free) with the ".NET desktop development" workload.
2. Open Visual Studio → **File → Open → Project/Folder** → select this `BorderlessApp` folder (the `.csproj` will be picked up automatically).
3. Press **F5** (or Ctrl+F5 to run without debugging).

Alternatively, from a terminal with the .NET SDK installed:
```
cd BorderlessApp
dotnet run
```

## Cutting a release (both .exe downloads)

A GitHub Actions workflow (`.github/workflows/release.yml`) builds both variants
and attaches them to a GitHub Release automatically. To trigger it, tag a
commit and push the tag:

```
git tag v1.0.0
git push origin v1.0.0
```

That kicks off the workflow, which:
1. Builds a **framework-dependent** app (needs the .NET 8 Desktop Runtime already installed) — packaged as `BorderlessApp-FrameworkDependent.zip`, since this variant is several files (the exe plus its `.dll`/`.json` config) that all need to sit together in one folder
2. Builds a **self-contained single-file** exe (~150MB, no dependencies needed) as `BorderlessApp-SelfContained.exe` — genuinely one file, no zip needed
3. Packages the self-contained build into `BorderlessApp-Setup.exe` (via Inno Setup) — a proper installer with Start Menu/desktop shortcuts that registers in Windows Settings > Apps, so it can be uninstalled cleanly from there
4. Publishes a GitHub Release for that tag with all three attached

The installer script is `installer.iss`. Its `AppId` (a GUID) must stay
identical across every future release — that's how Windows recognizes a
new version as an *upgrade* of the same app rather than a separate program.
It's already set; just don't change it. Version numbers are stamped in
automatically from the git tag you push, so you don't need to edit the
script for each release.

Watch it run under your repo's **Actions** tab. Once it finishes, the release
shows up under **Releases** with both files ready to download.

To cut a new release later, bump the version and push a new tag (e.g. `v1.0.1`) —
no manual building required.

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
- Games using **exclusive fullscreen** internally will fight this — make sure the game itself is set to "Windowed" in its own graphics settings first.
- The tray icon uses a placeholder system icon — swap in a real `.ico` file for a polished look.
- If you checked **Start with Windows** inside the app, that adds its own `HKCU...\Run` registry entry independently of the installer. Uninstalling via **Settings > Apps** removes the app itself, but won't know to clean up that entry — uncheck the box in-app before uninstalling to avoid a stale startup entry.
