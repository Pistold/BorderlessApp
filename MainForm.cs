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
            Width = 440;
            Height = 440;
            StartPosition = FormStartPosition.CenterScreen;
            FormClosing += MainForm_FormClosing;
            Resize += MainForm_Resize;

            // Pull the icon that's compiled into the exe (via <ApplicationIcon>
            // in the .csproj) rather than loading the .ico file separately -
            // this way it works the same whether running the framework-
            // dependent, self-contained, or installed build.
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;

            _profileListBox.SetBounds(12, 12, 400, 220);
            Controls.Add(_profileListBox);

            _addButton.Text = "Add Running Application...";
            _addButton.SetBounds(12, 244, 190, 30);
            _addButton.Click += AddButton_Click;
            Controls.Add(_addButton);

            _removeButton.Text = "Remove Selected";
            _removeButton.SetBounds(210, 244, 130, 30);
            _removeButton.Click += RemoveButton_Click;
            Controls.Add(_removeButton);

            _startupCheckBox.Text = "Start with Windows";
            _startupCheckBox.SetBounds(12, 290, 200, 24);
            _startupCheckBox.CheckedChanged += StartupCheckBox_CheckedChanged;
            Controls.Add(_startupCheckBox);

            // Deliberately separated from the Add/Remove row and colored as
            // a caution action so it's not an easy misclick.
            _uninstallButton.Text = "Uninstall App...";
            _uninstallButton.SetBounds(12, 322, 150, 28);
            _uninstallButton.ForeColor = Color.DarkRed;
            _uninstallButton.Click += UninstallButton_Click;
            Controls.Add(_uninstallButton);

            var infoLabel = new Label
            {
                Text = "Closing this window minimizes it to the tray - it keeps\n" +
                       "watching for your saved games in the background.\n" +
                       "Use the tray icon's \"Exit\" to fully quit.",
                ForeColor = Color.DimGray
            };
            infoLabel.SetBounds(12, 360, 400, 50);
            Controls.Add(infoLabel);
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
            _trayIcon.ContextMenuStrip = menu;
        }

        private void RestoreFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
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
            int selectedIndex = _profileListBox.SelectedIndex;

            _profileListBox.Items.Clear();
            foreach (var profile in _profiles)
            {
                var matches = Process.GetProcessesByName(profile.ProcessName);
                bool running = matches.Length > 0;
                foreach (var p in matches) p.Dispose();

                string status = running ? "Running" : "Not running";
                _profileListBox.Items.Add($"{profile.DisplayName}   [{profile.ProcessName}.exe]   -   {status}");
            }

            if (selectedIndex >= 0 && selectedIndex < _profileListBox.Items.Count)
                _profileListBox.SelectedIndex = selectedIndex;
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

            using var pickerForm = new Form
            {
                Text = "Select a Running Application",
                Width = 400,
                Height = 340,
                StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false,
                MaximizeBox = false
            };

            var listBox = new ListBox();
            listBox.SetBounds(10, 10, 360, 230);
            foreach (var proc in candidates)
                listBox.Items.Add($"{proc.MainWindowTitle}  ({proc.ProcessName}.exe)");
            pickerForm.Controls.Add(listBox);

            var okButton = new Button { Text = "Add Selected", DialogResult = DialogResult.OK };
            okButton.SetBounds(10, 250, 150, 30);
            pickerForm.Controls.Add(okButton);
            pickerForm.AcceptButton = okButton;

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
                        DisplayName = chosen.MainWindowTitle
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
                        if (proc.MainWindowHandle == IntPtr.Zero) continue;

                        // Only touch it if it still has a border - avoids
                        // hammering SetWindowPos every 1.5s once it's done.
                        if (WindowHelper.HasBorder(proc.MainWindowHandle))
                        {
                            // Capture fresh every time it's found bordered
                            // (a relaunch might be at a different resolution
                            // than last time), and persist it onto the
                            // profile so Remove Selected can restore it
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
