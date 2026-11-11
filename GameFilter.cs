using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace BorderlessApp
{
    // Best-effort "does this process look like a game?" check.
    //
    // Windows has no real flag for this, so it's a heuristic: known
    // non-games are ruled out, then it looks for positive signals (living in
    // a game library folder, an engine's folder layout, or a graphics API
    // loaded into the process). It will occasionally miss a game or let a
    // non-game through - which is why the picker has a "Games only" toggle
    // instead of hiding things permanently.
    public static class GameFilter
    {
        // Common windowed apps that load graphics libraries but aren't games,
        // plus the game launchers themselves.
        private static readonly HashSet<string> NotGames = new(StringComparer.OrdinalIgnoreCase)
        {
            // Windows shell
            "explorer", "ApplicationFrameHost", "TextInputHost", "SearchHost",
            "StartMenuExperienceHost", "ShellExperienceHost", "SystemSettings", "LockApp",
            // Browsers
            "chrome", "msedge", "firefox", "brave", "opera", "vivaldi",
            // Chat / media / productivity
            "discord", "slack", "teams", "ms-teams", "spotify", "zoom",
            // Dev tools and terminals
            "code", "devenv", "rider64", "idea64", "notepad++", "WindowsTerminal",
            "cmd", "powershell", "pwsh",
            // Game launchers / clients (not the games themselves)
            "steam", "steamwebhelper", "EpicGamesLauncher", "Battle.net", "GalaxyClient",
            "upc", "UbisoftConnect", "EADesktop", "Origin", "RiotClientServices", "RiotClientUx",
            // This app
            "BorderlessApp"
        };

        // Lowercase path fragments that strongly suggest a game install.
        private static readonly string[] GameFolderMarkers =
        {
            @"\steamapps\common\",
            @"\epic games\",
            @"\gog galaxy\games\",
            @"\gog games\",
            @"\ubisoft game launcher\games\",
            @"\xboxgames\",
            @"\riot games\",
            @"\ea games\",
            @"\origin games\",
            @"\rockstar games\",
            @"\games\",
            @"\binaries\win64\", // Unreal Engine layout
            @"\binaries\win32\"
        };

        // Modules that games load but ordinary windowed apps mostly don't.
        // (dxgi and opengl32 are deliberately left out - far too many
        // non-games load them.)
        private static readonly HashSet<string> GraphicsModules = new(StringComparer.OrdinalIgnoreCase)
        {
            "d3d9.dll", "d3d11.dll", "d3d12.dll", "vulkan-1.dll", "UnityPlayer.dll"
        };

        public static bool LooksLikeGame(Process proc, string exePath)
        {
            // If we can't even read where the exe lives (protected/elevated
            // process), we can't vouch for it. It still shows up with the
            // filter off.
            if (string.IsNullOrEmpty(exePath))
                return false;

            try
            {
                if (NotGames.Contains(proc.ProcessName))
                    return false;
            }
            catch
            {
                return false;
            }

            // Anything inside the Windows folder is part of the OS.
            string windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrEmpty(windowsDir) &&
                exePath.StartsWith(windowsDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return false;

            string normalized = exePath.Replace('/', '\\').ToLowerInvariant();
            foreach (var marker in GameFolderMarkers)
            {
                if (normalized.Contains(marker))
                    return true;
            }

            return LoadsGraphicsModule(proc);
        }

        private static bool LoadsGraphicsModule(Process proc)
        {
            try
            {
                foreach (ProcessModule module in proc.Modules)
                {
                    using (module)
                    {
                        if (GraphicsModules.Contains(module.ModuleName))
                            return true;
                    }
                }
            }
            catch
            {
                // Access denied / process exiting - treat as "no signal".
            }

            return false;
        }
    }
}
