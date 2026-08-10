using MudBlazor;

namespace WeekendDrops.Web.Shared;

public static class WdTheme
{
    public static readonly MudTheme Theme = new()
    {
        PaletteDark = new PaletteDark
        {
            Primary = "#CD8532",
            Secondary = "#6BA361",
            Tertiary = "#8D8D92",

            Background = "#111112",
            Surface = "#1C1C1E",
            AppbarBackground = "#0E0E10",
            AppbarText = "#DDDDDF",
            DrawerBackground = "#1C1C1E",

            TextPrimary = "#DDDDDF",
            TextSecondary = "#9A9AA2",
            ActionDefault = "#9A9AA2",
            Divider = "#323234",
            DividerLight = "#26262A",
            LinesDefault = "#323234",
            TableLines = "#26262A",
            TableHover = "#212127",

            Success = "#6BA361",
            Warning = "#CD8532",
            Error = "#C74A3C",
            Info = "#5B8DEF"
        },

        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "4px"
        }
    };
}
