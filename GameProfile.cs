using System.Drawing;

namespace BorderlessApp
{
    // A single saved "make this borderless" entry.
    // We store the process name (e.g. "eldenring") rather than a window
    // handle, since handles are only valid for one running instance and
    // change every time the game is launched.
    public class GameProfile
    {
        public string ProcessName { get; set; } = "";
        public string DisplayName { get; set; } = "";

        // The window's size/position from just before it was made
        // borderless, so it can be put back exactly if the profile is
        // removed. Persisted (not just kept in memory) so this still works
        // even if the app was restarted since the game was last detected.
        public int? OriginalX { get; set; }
        public int? OriginalY { get; set; }
        public int? OriginalWidth { get; set; }
        public int? OriginalHeight { get; set; }

        public Rectangle? GetOriginalBounds()
        {
            if (OriginalWidth.HasValue && OriginalHeight.HasValue)
                return new Rectangle(OriginalX ?? 0, OriginalY ?? 0, OriginalWidth.Value, OriginalHeight.Value);

            return null;
        }

        public void SetOriginalBounds(Rectangle bounds)
        {
            OriginalX = bounds.X;
            OriginalY = bounds.Y;
            OriginalWidth = bounds.Width;
            OriginalHeight = bounds.Height;
        }
    }
}
