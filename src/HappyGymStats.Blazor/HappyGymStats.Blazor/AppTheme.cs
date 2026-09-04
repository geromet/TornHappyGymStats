using MudBlazor;

namespace HappyGymStats.Blazor;

/// <summary>
/// Single source of truth for the application's dark theme.
/// All MudThemeProvider instances must reference this; do not define a second palette.
/// </summary>
internal static class AppTheme
{
    public static readonly MudTheme Theme = new()
    {
        PaletteDark = new PaletteDark
        {
            // Interactive accents
            Primary = "#58a6ff",
            PrimaryContrastText = "#041426",
            Secondary = "#4da8da",
            SecondaryContrastText = "#041426",

            // Semantic state — also drives --mud-palette-* CSS variables used in app.css / War.razor.css
            Error = "#ef5350",
            Warning = "#ff9800",
            Info = "#29b6f6",
            Success = "#66bb6a",

            // Canvas
            Background = "#0b1020",
            BackgroundGray = "#0d1529",
            Surface = "#121a2f",
            AppbarBackground = "#0d1529",
            DrawerBackground = "#0d1529",

            // Text
            TextPrimary = "#e8f1ff",
            TextSecondary = "#b8e1ff",

            // Borders / grid lines
            TableLines = "#243457",
            Divider = "#243457",
            LinesDefault = "#243457",
        }
    };
}
