using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BorderlessApp
{
    public enum ButtonKind { Primary, Secondary, Danger }

    // One place for the app's whole look. The palette follows the Windows
    // "app mode" (light/dark) setting, read once at startup - so if you
    // switch Windows themes, restart the app to pick it up.
    public static class Theme
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string? pszSubAppName, string? pszSubIdList);

        public static readonly bool IsDark;

        public static readonly Color Background;
        public static readonly Color Surface;
        public static readonly Color SelectedRow;
        public static readonly Color Border;
        public static readonly Color TextPrimary;
        public static readonly Color TextSecondary;
        public static readonly Color Accent;
        public static readonly Color AccentHover;
        public static readonly Color Danger;
        public static readonly Color DangerSoft;
        public static readonly Color Success;
        public static readonly Color Muted;

        public static readonly Font Title;
        public static readonly Font Body;
        public static readonly Font BodyStrong;
        public static readonly Font Small;

        static Theme()
        {
            IsDark = DetectDarkMode();

            if (IsDark)
            {
                Background = Color.FromArgb(24, 25, 29);
                Surface = Color.FromArgb(33, 34, 40);
                SelectedRow = Color.FromArgb(48, 52, 64);
                Border = Color.FromArgb(56, 58, 68);
                TextPrimary = Color.FromArgb(236, 237, 241);
                TextSecondary = Color.FromArgb(146, 149, 160);
                Accent = Color.FromArgb(76, 140, 255);
                AccentHover = Color.FromArgb(104, 160, 255);
                Danger = Color.FromArgb(240, 98, 98);
                DangerSoft = Color.FromArgb(58, 32, 36);
                Success = Color.FromArgb(63, 200, 110);
                Muted = Color.FromArgb(98, 101, 112);
            }
            else
            {
                Background = Color.FromArgb(243, 244, 247);
                Surface = Color.FromArgb(255, 255, 255);
                SelectedRow = Color.FromArgb(225, 236, 255);
                Border = Color.FromArgb(219, 221, 228);
                TextPrimary = Color.FromArgb(28, 30, 36);
                TextSecondary = Color.FromArgb(104, 108, 120);
                Accent = Color.FromArgb(0, 103, 192);
                AccentHover = Color.FromArgb(20, 122, 214);
                Danger = Color.FromArgb(196, 43, 28);
                DangerSoft = Color.FromArgb(253, 236, 234);
                Success = Color.FromArgb(22, 163, 74);
                Muted = Color.FromArgb(170, 173, 182);
            }

            Title = new Font("Segoe UI Semibold", 17f);
            Body = new Font("Segoe UI", 9.5f);
            BodyStrong = new Font("Segoe UI Semibold", 10f);
            Small = new Font("Segoe UI", 8.5f);
        }

        private static bool DetectDarkMode()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
            }
            catch
            {
                return false;
            }
        }

        // Dark title bar to match the window body. Call once the form's
        // handle exists (OnHandleCreated). No-op in light mode.
        public static void ApplyTitleBar(Form form)
        {
            if (!IsDark) return;

            try
            {
                int on = 1;
                // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE (Win10 20H1+ / Win11);
                // 19 is the same flag on earlier Win10 builds.
                if (DwmSetWindowAttribute(form.Handle, 20, ref on, sizeof(int)) != 0)
                    DwmSetWindowAttribute(form.Handle, 19, ref on, sizeof(int));
            }
            catch
            {
                // Cosmetic only - never worth crashing over.
            }
        }

        // Dark scrollbars for a list. No-op in light mode.
        public static void ApplyDarkScrollbars(Control control)
        {
            if (!IsDark) return;

            try
            {
                SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
            }
            catch
            {
                // Cosmetic only.
            }
        }

        public static void StyleButton(Button button, ButtonKind kind)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.UseVisualStyleBackColor = false;
            button.Font = BodyStrong;
            button.Cursor = Cursors.Hand;
            button.FlatAppearance.BorderSize = 1;

            switch (kind)
            {
                case ButtonKind.Primary:
                    button.BackColor = Accent;
                    button.ForeColor = Color.White;
                    button.FlatAppearance.BorderColor = Accent;
                    button.FlatAppearance.MouseOverBackColor = AccentHover;
                    button.FlatAppearance.MouseDownBackColor = AccentHover;
                    break;

                case ButtonKind.Secondary:
                    button.BackColor = Surface;
                    button.ForeColor = TextPrimary;
                    button.FlatAppearance.BorderColor = Border;
                    button.FlatAppearance.MouseOverBackColor = SelectedRow;
                    button.FlatAppearance.MouseDownBackColor = SelectedRow;
                    break;

                case ButtonKind.Danger:
                    button.BackColor = Background;
                    button.ForeColor = Danger;
                    button.FlatAppearance.BorderColor = Danger;
                    button.FlatAppearance.MouseOverBackColor = DangerSoft;
                    button.FlatAppearance.MouseDownBackColor = DangerSoft;
                    break;
            }
        }

        // Dark/light styling for the tray icon's right-click menu.
        public static void StyleMenu(ContextMenuStrip menu)
        {
            menu.Renderer = new ToolStripProfessionalRenderer(new MenuColors()) { RoundedEdges = false };
            menu.BackColor = Surface;
            menu.ForeColor = TextPrimary;
            menu.Font = Body;

            foreach (ToolStripItem item in menu.Items)
            {
                item.ForeColor = TextPrimary;
                item.Padding = new Padding(4, 6, 4, 6);
            }
        }

        // Paints one two-line list row: icon, title, subtitle, and an
        // optional status dot + label on the right. Shared by the main
        // game list and the "Add Running Game" picker so they match.
        public static void DrawRow(Graphics g, Rectangle bounds, bool selected, Image icon,
            string title, string subtitle, string? status = null, bool running = false)
        {
            using (var background = new SolidBrush(Surface))
                g.FillRectangle(background, bounds);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            var card = Rectangle.Inflate(bounds, -6, -3);

            if (selected)
            {
                using var path = RoundedRect(card, 8);
                using var highlight = new SolidBrush(SelectedRow);
                g.FillPath(highlight, path);
            }
            else
            {
                // Thin divider between unselected rows.
                using var divider = new Pen(Border);
                g.DrawLine(divider, bounds.X + 18, bounds.Bottom - 1, bounds.Right - 18, bounds.Bottom - 1);
            }

            const int iconSize = 32;
            var iconRect = new Rectangle(
                card.X + 10, card.Y + (card.Height - iconSize) / 2, iconSize, iconSize);
            g.DrawImage(icon, iconRect);

            const int rightPadding = 14;
            const int dotSize = 8;
            const int dotGap = 6;

            int statusBlockWidth = 0;
            if (status != null)
            {
                int textWidth = TextRenderer.MeasureText(
                    g, status, Small, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width;
                statusBlockWidth = dotSize + dotGap + textWidth;
            }

            int textX = iconRect.Right + 12;
            int textWidthAvailable = card.Right - textX - rightPadding
                - (statusBlockWidth > 0 ? statusBlockWidth + 12 : 0);

            const int titleHeight = 22;
            const int subtitleHeight = 18;
            int textTop = card.Y + (card.Height - (titleHeight + subtitleHeight)) / 2;

            const TextFormatFlags textFlags =
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;

            TextRenderer.DrawText(g, title, BodyStrong,
                new Rectangle(textX, textTop, textWidthAvailable, titleHeight), TextPrimary, textFlags);
            TextRenderer.DrawText(g, subtitle, Small,
                new Rectangle(textX, textTop + titleHeight, textWidthAvailable, subtitleHeight), TextSecondary, textFlags);

            if (status != null)
            {
                int blockX = card.Right - rightPadding - statusBlockWidth;

                using (var dotBrush = new SolidBrush(running ? Success : Muted))
                    g.FillEllipse(dotBrush,
                        new Rectangle(blockX, card.Y + (card.Height - dotSize) / 2, dotSize, dotSize));

                TextRenderer.DrawText(g, status, Small,
                    new Rectangle(blockX + dotSize + dotGap, card.Y, card.Right - rightPadding - (blockX + dotSize + dotGap), card.Height),
                    running ? TextPrimary : TextSecondary,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d - 1, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d - 1, r.Bottom - d - 1, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d - 1, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private sealed class MenuColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => Surface;
            public override Color ImageMarginGradientBegin => Surface;
            public override Color ImageMarginGradientMiddle => Surface;
            public override Color ImageMarginGradientEnd => Surface;
            public override Color MenuItemSelected => SelectedRow;
            public override Color MenuItemSelectedGradientBegin => SelectedRow;
            public override Color MenuItemSelectedGradientEnd => SelectedRow;
            public override Color MenuItemBorder => SelectedRow;
            public override Color MenuBorder => Border;
            public override Color SeparatorDark => Border;
            public override Color SeparatorLight => Border;
        }
    }
}
