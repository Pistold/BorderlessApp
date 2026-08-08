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
    }
}
