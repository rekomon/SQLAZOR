using MudBlazor;

namespace SQLAZOR.Portal.Helper
{
    public class MudCustomTheme : MudTheme
    {
        public PaletteLight paletteLight { get; } = new PaletteLight
        {
            Primary = "#00f0ff",        // Neon Cyan
            Secondary = "#ff00ff",      // Neon Magenta
            Tertiary = "#ffff00",       // Neon Yellow
            Background = "#e8f0ff",     // Light mode background
            Surface = "#ffffff",        // Light mode surface (cards, etc.)
            TextPrimary = "#0a1a2a",    // Light mode primary text
            TextSecondary = "rgba(10, 30, 50, 0.8)",
            AppbarBackground = "rgba(255, 255, 255, 0.85)",
            DrawerBackground = "rgba(255, 255, 255, 0.85)",

        };
        public PaletteDark paletteDark { get; } = new PaletteDark
        {
            Primary = "#00f0ff",
            Secondary = "#ff00ff",
            Tertiary = "#ffff00",
            Background = "#0a0a0f",     // Dark mode background
            Surface = "rgba(0, 20, 30, 0.7)", // Dark mode surface
            TextPrimary = "#e0f0ff",    // Dark mode primary text
            TextSecondary = "rgba(200, 230, 255, 0.8)",
            AppbarBackground = "rgba(0, 20, 30, 0.7)",
            DrawerBackground = "rgba(0, 20, 30, 0.7)",
        };
    }
}
