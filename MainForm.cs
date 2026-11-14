using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BorderlessApp
{
    public class MainForm : Form
    {
        private readonly ListBox _profileListBox = new ListBox();
        private readonly Button _addButton = new Button();
        private readonly Button _removeButton = new Button();
        private readonly CheckBox _startupCheckBox = new CheckBox();
        private readonly Button _uninstallButton = new Button();
        private readonly NotifyIcon _trayIcon = new NotifyIcon();
        private readonly System.Windows.Forms.Timer _watcherTimer = new System.Windows.Forms.Timer();
        private readonly List<GameProfile> _profiles;
        private bool _isExiting;

        // Game icons pulled from each game's exe, keyed by process name.
        // Only successful lookups are cached, so a game whose exe path
        // wasn't known yet gets retried once it is.
        private readonly Dictionary<string, Bitmap> _gameIcons =
            new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        private readonly Bitmap _fallbackIcon = SystemIcons.Application.ToBitmap();

        // Shown over the list while it's empty.
        private readonly Label _emptyLabel = new Label();

        // Which saved games are running right now - refreshed by
        // RefreshProfileList and read when painting each row's status dot.
        private readonly HashSet<string> _runningNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private const string StartupRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string StartupValueName = "BorderlessWindowManager";

        public MainForm()
        {
            _profiles = ConfigManager.Load();

            SetupUI();
            SetupTrayIcon();
            RefreshProfileList();

            _startupCheckBox.Checked = IsStartupEnabled();

            _watcherTimer.Interval = 1500; // check twice a second is overkill; 1.5s is plenty
            _watcherTimer.Tick += WatcherTimer_Tick;
            _watcherTimer.Start();
        }

        // ----- UI setup -----

        private void SetupUI()
        {
            Text = "Borderless Window Manager";
            ClientSize = new Size(480, 580);
            MinimumSize = new Size(440, 520);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Background;
            ForeColor = Theme.TextPrimary;
            Font = Theme.Body;
            FormClosing += MainForm_FormClosing;
            Resize += MainForm_Resize;

            // Pull the icon that's compiled into the exe (via <ApplicationIcon>
            // in the .csproj) rather than loading the .ico file separately -
            // this way it works the same whether running the framework-
            // dependent, self-contained, or installed build.
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;

            // Whole window is one column: header / game list / buttons / footer.
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(20, 18, 20, 16),
                BackColor = Theme.Background
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // --- Header ---
            var header = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Margin = new Padding(0)
            };
            header.Controls.Add(new Label
            {
                Text = "Borderless Window Manager",
                Font = Theme.Title,
                ForeColor = Theme.TextPrimary,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 2)
            });
            header.Controls.Add(new Label
            {
                Text = "Pick a game once. It stays borderless every time it launches.",
                Font = Theme.Body,
                ForeColor = Theme.TextSecondary,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 16)
            });
            layout.Controls.Add(header, 0, 0);

            // --- Game list (1px border via the card panel's padding) ---
            var listCard = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.Border,
                Padding = new Padding(1),
                Margin = new Padding(0)
            };

            _profileListBox.Dock = DockStyle.Fill;
            _profileListBox.BorderStyle = BorderStyle.None;
            _profileListBox.BackColor = Theme.Surface;
            _profileListBox.DrawMode = DrawMode.OwnerDrawFixed; // we paint each row ourselves (icon, status dot, etc.)
            _profileListBox.ItemHeight = 56;
            _profileListBox.IntegralHeight = false;
            _profileListBox.DrawItem += ProfileListBox_DrawItem;
            _profileListBox.HandleCreated += (s, e) => Theme.ApplyDarkScrollbars(_profileListBox);

            // Shown over the list while there are no saved games yet.
            _emptyLabel.Dock = DockStyle.Fill;
            _emptyLabel.TextAlign = ContentAlignment.MiddleCenter;
            _emptyLabel.BackColor = Theme.Surface;
            _emptyLabel.ForeColor = Theme.TextSecondary;
            _emptyLabel.Font = Theme.Body;
            _emptyLabel.Text = "No games added yet\n\nLaunch a game in windowed mode,\nthen click Add Running Game.";
            _profileListBox.Controls.Add(_emptyLabel);

            listCard.Controls.Add(_profileListBox);
            layout.Controls.Add(listCard, 0, 1);

            // --- Add / Remove buttons ---
            var buttonRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0)
            };

            _addButton.Text = "Add Running Game";
            _addButton.Size = new Size(200, 40);
            _addButton.Margin = new Padding(0, 14, 10, 0);
            Theme.StyleButton(_addButton, ButtonKind.Primary);
            _addButton.Click += AddButton_Click;
            buttonRow.Controls.Add(_addButton);

            _removeButton.Text = "Remove Selected";
            _removeButton.Size = new Size(160, 40);
            _removeButton.Margin = new Padding(0, 14, 0, 0);
            Theme.StyleButton(_removeButton, ButtonKind.Secondary);
            _removeButton.Click += RemoveButton_Click;
            buttonRow.Controls.Add(_removeButton);

            layout.Controls.Add(buttonRow, 0, 2);

            // --- Footer: startup toggle, uninstall, and a hint ---
            var footer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 2,
                Margin = new Padding(0, 16, 0, 0)
            };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _startupCheckBox.Text = "Start with Windows";
            _startupCheckBox.AutoSize = true;
            _startupCheckBox.Anchor = AnchorStyles.Left;
            _startupCheckBox.ForeColor = Theme.TextPrimary;
            _startupCheckBox.CheckedChanged += StartupCheckBox_CheckedChanged;
            footer.Controls.Add(_startupCheckBox, 0, 0);

            // Deliberately a muted outline button off to the side, colored
            // as a caution action so it's not an easy misclick.
            _uninstallButton.Text = "Uninstall App...";
            _uninstallButton.Size = new Size(140, 34);
            _uninstallButton.Anchor = AnchorStyles.Right;
            Theme.StyleButton(_uninstallButton, ButtonKind.Danger);
            _uninstallButton.Click += UninstallButton_Click;
            footer.Controls.Add(_uninstallButton, 1, 0);

            var hint = new Label
            {
                Text = "Closing this window keeps the app running in the tray.\nUse the tray icon's Exit to quit fully.",
                Font = Theme.Small,
                ForeColor = Theme.TextSecondary,
                AutoSize = true,
                Margin = new Padding(0, 10, 0, 0)
            };
            footer.Controls.Add(hint, 0, 1);
            footer.SetColumnSpan(hint, 2);

            layout.Controls.Add(footer, 0, 3);

            Controls.Add(layout);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.ApplyTitleBar(this); // dark title bar in dark mode
        }

        private void SetupTrayIcon()
        {
            _trayIcon.Icon = Icon; // same icon set on the form in SetupUI
            _trayIcon.Text = "Borderless Window Manager";
            _trayIcon.Visible = true;
            _trayIcon.DoubleClick += (s, e) => RestoreFromTray();

            var menu = new ContextMenuStrip();
            menu.Items.Add("Open", null, (s, e) => RestoreFromTray());
            menu.Items.Add("Exit", null, (s, e) =>
            {
                _isExiting = true;
                Close();
            });
            Theme.StyleMenu(menu);
            _trayIcon.ContextMenuStrip = menu;
        }

        private void RestoreFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        protected override void WndProc(ref Message m)
        {
            // A second launch attempt (blocked by the single-instance mutex
            // in Program.cs) broadcasts this to ask us to come back into view.
            if ((uint)m.Msg == WindowHelper.ShowExistingInstanceMessage)
                RestoreFromTray();

            base.WndProc(ref m);
        }

        private void MainForm_Resize(object? sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized)
                Hide();
        }

        private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            if (!_isExiting)
            {
                // "X" button just hides to tray so the watcher keeps running.
                e.Cancel = true;
                Hide();
                return;
            }

            _trayIcon.Visible = false;
            _watcherTimer.Stop();
        }

        // ----- Profile list -----

        private void RefreshProfileList()
        {
            var rows = new List<string>();
            _runningNames.Clear();
            foreach (var profile in _profiles)
            {
                var matches = Process.GetProcessesByName(profile.ProcessName);
                bool running = matches.Length > 0;
                foreach (var p in matches) p.Dispose();

                if (running) _runningNames.Add(profile.ProcessName);

                // Rows are painted by ProfileListBox_DrawItem from the
                // profile itself - this string only exists so we can tell
                // whether anything changed since the last refresh.
                rows.Add($"{profile.DisplayName}|{profile.ProcessName}|{running}");
            }

            _emptyLabel.Visible = _profiles.Count == 0;

            // This runs every watcher tick. Rebuilding an owner-drawn list
            // that often makes it flicker, so only touch it if a row
            // actually changed.
            bool unchanged = rows.Count == _profileListBox.Items.Count;
            for (int i = 0; unchanged && i < rows.Count; i++)
                unchanged = (string?)_profileListBox.Items[i] == rows[i];
            if (unchanged) return;

            int selectedIndex = _profileListBox.SelectedIndex;

            _profileListBox.BeginUpdate();
            _profileListBox.Items.Clear();
            foreach (var row in rows)
                _profileListBox.Items.Add(row);
            _profileListBox.EndUpdate();

            if (selectedIndex >= 0 && selectedIndex < _profileListBox.Items.Count)
                _profileListBox.SelectedIndex = selectedIndex;
        }

        private void ProfileListBox_DrawItem(object? sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _profiles.Count)
            {
                e.DrawBackground();
                return;
            }

            var profile = _profiles[e.Index];
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            bool running = _runningNames.Contains(profile.ProcessName);

            Theme.DrawRow(
                e.Graphics, e.Bounds, selected, GetGameIcon(profile),
                profile.DisplayName, $"{profile.ProcessName}.exe",
                running ? "Running" : "Not running", running);
        }

        // The game's own icon pulled from its exe. Falls back to a generic
        // icon if the path isn't known yet or the exe can't be read.
        private Bitmap GetGameIcon(GameProfile profile)
        {
            if (_gameIcons.TryGetValue(profile.ProcessName, out var cached))
                return cached;

            if (!string.IsNullOrEmpty(profile.ExecutablePath) && File.Exists(profile.ExecutablePath))
            {
                try
                {
                    using var extracted = Icon.ExtractAssociatedIcon(profile.ExecutablePath);
                    if (extracted != null)
                    {
                        var bitmap = extracted.ToBitmap();
                        _gameIcons[profile.ProcessName] = bitmap;
                        return bitmap;
                    }
                }
                catch
                {
                    // Unreadable exe - just use the fallback.
                }
            }

            return _fallbackIcon;
        }

        // Reading a process's exe path can throw (protected/elevated
        // processes, or one that's mid-exit), so never let it propagate.
        private static string TryGetExePath(Process proc)
        {
            try
            {
                return proc.MainModule?.FileName ?? "";
            }
            catch
            {
                return "";
            }
        }

        // Loads the icon embedded in an exe as a Bitmap, or null if the path
        // is empty/unreadable. Caller owns (and should dispose) the bitmap.
        private static Bitmap? TryLoadIcon(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;

            try
            {
                using var extracted = Icon.ExtractAssociatedIcon(path);
                return extracted?.ToBitmap();
            }
            catch
            {
                return null;
            }
        }

        private void AddButton_Click(object? sender, EventArgs e)
        {
            var allProcs = Process.GetProcesses();
            var candidates = new List<Process>();
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var p in allProcs)
            {
                try
                {
                    // Touching properties on a process that's mid-exit, or
                    // one that's elevated/protected, can throw. One bad
                    // process here shouldn't stop us from checking the rest -
                    // a LINQ .Where() chain would abort entirely on the
                    // first exception, silently hiding everything after it
                    // in enumeration order (including anything you just
                    // launched).
                    if (p.MainWindowHandle == IntPtr.Zero) continue;
                    if (string.IsNullOrWhiteSpace(p.MainWindowTitle)) continue;
                    if (!seenNames.Add(p.ProcessName)) continue; // de-dupe by process name

                    candidates.Add(p);
                }
                catch
                {
                    // Skip it and keep going.
                }
            }

            candidates = candidates.OrderBy(p => p.ProcessName).ToList();

            // Snapshot titles/names/icons up front so painting the dialog
            // never has to touch a Process that may have exited while running
            var titles = candidates.Select(c =>
            {
                try { return c.MainWindowTitle; }
                catch { return c.ProcessName; }
            }).ToList();
            var exeNames = candidates.Select(c => $"{c.ProcessName}.exe").ToList();
            var candidateIcons = candidates
                .Select(c => TryLoadIcon(TryGetExePath(c)) ?? _fallbackIcon)
                .ToList();

            using var pickerForm = new Form
            {
                Text = "Select a Running Game",
                ClientSize = new Size(460, 440),
                MinimumSize = new Size(380, 360),
                StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false,
                MaximizeBox = false,
                ShowInTaskbar = false,
                BackColor = Theme.Background,
                ForeColor = Theme.TextPrimary,
                Font = Theme.Body
            };
            pickerForm.Icon = Icon;
            pickerForm.HandleCreated += (s, args) => Theme.ApplyTitleBar(pickerForm);

            var pickerLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(16),
                BackColor = Theme.Background
            };
            pickerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            pickerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            pickerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            pickerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            pickerLayout.Controls.Add(new Label
            {
                Text = "Choose the game window to make borderless.",
                ForeColor = Theme.TextSecondary,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 10)
            }, 0, 0);

            var pickerCard = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.Border,
                Padding = new Padding(1),
                Margin = new Padding(0)
            };

            var listBox = new ListBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = Theme.Surface,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 56,
                IntegralHeight = false
            };
            foreach (var title in titles)
                listBox.Items.Add(title);
            listBox.HandleCreated += (s, args) => Theme.ApplyDarkScrollbars(listBox);
            listBox.DrawItem += (s, args) =>
            {
                if (args.Index < 0 || args.Index >= candidates.Count) return;

                bool selected = (args.State & DrawItemState.Selected) == DrawItemState.Selected;
                Theme.DrawRow(args.Graphics, args.Bounds, selected,
                    candidateIcons[args.Index], titles[args.Index], exeNames[args.Index]);
            };
            pickerCard.Controls.Add(listBox);
            pickerLayout.Controls.Add(pickerCard, 0, 1);

            // Right-aligned Add / Cancel (RightToLeft flow: first added sits rightmost).
            var pickerButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Margin = new Padding(0)
            };

            var okButton = new Button
            {
                Text = "Add Selected",
                DialogResult = DialogResult.OK,
                Size = new Size(140, 38),
                Margin = new Padding(8, 14, 0, 0)
            };
            Theme.StyleButton(okButton, ButtonKind.Primary);
            pickerButtons.Controls.Add(okButton);

            var cancelButton = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Size = new Size(100, 38),
                Margin = new Padding(0, 14, 0, 0)
            };
            Theme.StyleButton(cancelButton, ButtonKind.Secondary);
            pickerButtons.Controls.Add(cancelButton);

            pickerLayout.Controls.Add(pickerButtons, 0, 2);
            pickerForm.Controls.Add(pickerLayout);
            pickerForm.AcceptButton = okButton;
            pickerForm.CancelButton = cancelButton;

            // Double-clicking a row is a shortcut for "Add Selected".
            listBox.DoubleClick += (s, args) =>
            {
                if (listBox.SelectedIndex >= 0)
                    pickerForm.DialogResult = DialogResult.OK;
            };

            var result = pickerForm.ShowDialog(this);

            if (result == DialogResult.OK && listBox.SelectedIndex >= 0)
            {
                var chosen = candidates[listBox.SelectedIndex];

                bool alreadySaved = _profiles.Any(p =>
                    p.ProcessName.Equals(chosen.ProcessName, StringComparison.OrdinalIgnoreCase));

                if (alreadySaved)
                {
                    MessageBox.Show(this, "That game is already saved.", "Already added");
                }
                else
                {
                    var newProfile = new GameProfile
                    {
                        ProcessName = chosen.ProcessName,
                        DisplayName = chosen.MainWindowTitle,
                        ExecutablePath = TryGetExePath(chosen)
                    };

                    // It's already running, so capture its current size/
                    // position and apply immediately instead of waiting
                    // for the next watcher tick.
                    if (chosen.MainWindowHandle != IntPtr.Zero)
                    {
                        newProfile.SetOriginalBounds(WindowHelper.GetWindowBounds(chosen.MainWindowHandle));
                        WindowHelper.MakeBorderless(chosen.MainWindowHandle);
                    }

                    _profiles.Add(newProfile);
                    ConfigManager.Save(_profiles);
                    RefreshProfileList();
                }
            }

            foreach (var bitmap in candidateIcons)
            {
                if (!ReferenceEquals(bitmap, _fallbackIcon))
                    bitmap.Dispose();
            }

            foreach (var p in allProcs) p.Dispose();
        }

        private void RemoveButton_Click(object? sender, EventArgs e)
        {
            int index = _profileListBox.SelectedIndex;
            if (index < 0) return;

            var profile = _profiles[index];

            // If it's currently running, give its border (and original
            // size/position, if we remember it) back before forgetting it.
            var procs = Process.GetProcessesByName(profile.ProcessName);
            var savedBounds = profile.GetOriginalBounds();
            foreach (var proc in procs)
            {
                try
                {
                    if (proc.MainWindowHandle == IntPtr.Zero) continue;
                    WindowHelper.RestoreBorder(proc.MainWindowHandle, savedBounds);
                }
                finally
                {
                    proc.Dispose();
                }
            }

            if (_gameIcons.Remove(profile.ProcessName, out var oldIcon))
                oldIcon.Dispose();

            _profiles.RemoveAt(index);
            ConfigManager.Save(_profiles);
            RefreshProfileList();
        }

        private void UninstallButton_Click(object? sender, EventArgs e)
        {
            // If this is the properly-installed copy, Inno Setup drops its
            // generated uninstaller right next to the exe.
            string? exeDir = Path.GetDirectoryName(Application.ExecutablePath);
            string installedUninstallerPath = Path.Combine(exeDir ?? "", "unins000.exe");
            bool isInstalledCopy = File.Exists(installedUninstallerPath);

            string confirmMessage = isInstalledCopy
                ? "This will remove your saved games list and app settings, " +
                  "clear any startup entry, then open Windows' uninstaller to " +
                  "remove the app itself. This can't be undone.\n\nContinue?"
                : "This will remove your saved games list, app settings, and " +
                  "any startup entry. You'll still need to delete this app's " +
                  "files/folder yourself afterward. This can't be undone.\n\nContinue?";

            var confirm = MessageBox.Show(this, confirmMessage, "Uninstall Borderless Window Manager",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);

            if (confirm != DialogResult.Yes) return;

            _watcherTimer.Stop();

            // Give every currently-running saved game its border back
            // before we forget about all of them.
            foreach (var profile in _profiles)
            {
                var procs = Process.GetProcessesByName(profile.ProcessName);
                var savedBounds = profile.GetOriginalBounds();
                foreach (var proc in procs)
                {
                    try
                    {
                        if (proc.MainWindowHandle != IntPtr.Zero)
                            WindowHelper.RestoreBorder(proc.MainWindowHandle, savedBounds);
                    }
                    finally
                    {
                        proc.Dispose();
                    }
                }
            }

            // Remove the "Start with Windows" registry entry, if one was made.
            using (var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, true))
                key?.DeleteValue(StartupValueName, throwOnMissingValue: false);

            // Wipe the saved config folder in %AppData%.
            ConfigManager.DeleteAll();

            _isExiting = true;
            _trayIcon.Visible = false;

            if (isInstalledCopy)
            {
                // Hand off to Windows' real uninstaller so the Start Menu
                // shortcuts, Program Files copy, and Apps-list entry get
                // cleaned up too. It shows its own confirmation/progress UI.
                Process.Start(installedUninstallerPath);
            }
            else
            {
                MessageBox.Show(this,
                    "Settings and startup entries have been removed. You can " +
                    "now delete this app's files/folder manually.",
                    "Cleanup complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            Application.Exit();
        }

        // ----- Background watcher -----

        private void WatcherTimer_Tick(object? sender, EventArgs e)
        {
            bool configDirty = false;

            foreach (var profile in _profiles)
            {
                var procs = Process.GetProcessesByName(profile.ProcessName);
                foreach (var proc in procs)
                {
                    try
                    {
                        // Backfill the exe path for games saved before icons
                        // were supported (or whose path couldn't be read at
                        // the time), so their icon can show up.
                        if (string.IsNullOrEmpty(profile.ExecutablePath))
                        {
                            string path = TryGetExePath(proc);
                            if (!string.IsNullOrEmpty(path))
                            {
                                profile.ExecutablePath = path;
                                configDirty = true;
                                _profileListBox.Invalidate(); // repaint rows with the new icon
                            }
                        }

                        if (proc.MainWindowHandle == IntPtr.Zero) continue;

                        // Only touch it if it still has a border - avoids
                        // hammering SetWindowPos every 1.5s once it's done.
                        if (WindowHelper.HasBorder(proc.MainWindowHandle))
                        {
                            // Capture fresh every time it's found bordered
                            // (a relaunch might be at a different resolution than last time)
                            // and persist it onto the profile so Remove Selected can restore it
                            // correctly even after an app restart.
                            profile.SetOriginalBounds(WindowHelper.GetWindowBounds(proc.MainWindowHandle));
                            WindowHelper.MakeBorderless(proc.MainWindowHandle);
                            configDirty = true;
                        }
                    }
                    finally
                    {
                        proc.Dispose();
                    }
                }
            }

            if (configDirty)
                ConfigManager.Save(_profiles);

            RefreshProfileList();
        }

        // ----- Start with Windows -----

        private bool IsStartupEnabled()
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, false);
            return key?.GetValue(StartupValueName) != null;
        }

        private void StartupCheckBox_CheckedChanged(object? sender, EventArgs e)
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, true);
            if (key == null) return;

            if (_startupCheckBox.Checked)
                key.SetValue(StartupValueName, Application.ExecutablePath);
            else
                key.DeleteValue(StartupValueName, false);
        }
    }
}
