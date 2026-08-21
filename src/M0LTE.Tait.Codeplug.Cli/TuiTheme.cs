using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Attribute = Terminal.Gui.Drawing.Attribute;
using Terminal.Gui.ViewBase;

namespace M0LTE.Tait.Codeplug.Cli;

/// <summary>
/// The interactive mode's colours. Terminal.Gui's stock scheme is grey-on-black, which reads as
/// "unfinished" next to anything else in a modern terminal, so this replaces the app-wide schemes
/// (base, dialog, error, menu) with a dark slate palette and a blue accent, and hands out a few
/// per-widget schemes on top: green for read, amber for write (it is the destructive one), an inset
/// background for the editable port box.
///
/// Colours are true-colour (24-bit). Terminal.Gui maps them down on a terminal that can only do 16
/// or 256, so this stays legible on a plain console - it just looks better where the terminal is
/// capable, which in 2026 is nearly everywhere.
/// </summary>
internal static class TuiTheme
{
    // Slate background, GitHub-dark-ish. Chosen for contrast at small sizes rather than for flair:
    // body text on the panel is ~11:1, which stays readable over SSH on a washed-out laptop screen.
    private static readonly Color Bg = new("#0D1117");        // the window behind the panels
    private static readonly Color Panel = new("#161B22");     // panel/card interior
    private static readonly Color Inset = new("#010409");     // editable fields, punched into the panel
    private static readonly Color Text = new("#C9D1D9");
    private static readonly Color Dim = new("#7D8590");
    private static readonly Color Accent = new("#58A6FF");    // borders, titles, hotkeys
    private static readonly Color Green = new("#3FB950");     // read: safe
    private static readonly Color Amber = new("#D29922");     // write: think first
    private static readonly Color Red = new("#F85149");
    private static readonly Color SelectionBg = new("#1F6FEB");
    private static readonly Color White = new("#FFFFFF");
    private static readonly Color ErrorBg = new("#4D1315");

    /// <summary>Replace the app-wide schemes. Call once, after the application is initialised and
    /// before any view is built, so dialogs and message boxes - which build their own views - come
    /// out themed too.</summary>
    internal static void Apply()
    {
        SchemeManager.AddScheme("Base", new Scheme
        {
            Normal = new Attribute(Text, Bg),
            HotNormal = new Attribute(Accent, Bg, TextStyle.Bold),
            Focus = new Attribute(White, SelectionBg),
            HotFocus = new Attribute(White, SelectionBg, TextStyle.Bold),
            Active = new Attribute(Accent, Bg),
            HotActive = new Attribute(Accent, Bg, TextStyle.Bold),
            Highlight = new Attribute(White, SelectionBg),
            Editable = new Attribute(Text, Inset),
            ReadOnly = new Attribute(Dim, Bg),
            Disabled = new Attribute(Dim, Bg),
        });

        SchemeManager.AddScheme("Dialog", new Scheme
        {
            Normal = new Attribute(Text, Panel),
            HotNormal = new Attribute(Accent, Panel, TextStyle.Bold),
            Focus = new Attribute(White, SelectionBg),
            HotFocus = new Attribute(White, SelectionBg, TextStyle.Bold),
            Active = new Attribute(Accent, Panel),
            HotActive = new Attribute(Accent, Panel, TextStyle.Bold),
            Highlight = new Attribute(White, SelectionBg),
            Editable = new Attribute(Text, Inset),
            ReadOnly = new Attribute(Dim, Panel),
            Disabled = new Attribute(Dim, Panel),
        });

        SchemeManager.AddScheme("Error", new Scheme
        {
            Normal = new Attribute(White, ErrorBg),
            HotNormal = new Attribute(Amber, ErrorBg, TextStyle.Bold),
            Focus = new Attribute(ErrorBg, White),
            HotFocus = new Attribute(Red, White, TextStyle.Bold),
            Active = new Attribute(White, ErrorBg),
            HotActive = new Attribute(Amber, ErrorBg, TextStyle.Bold),
            Highlight = new Attribute(ErrorBg, White),
            Disabled = new Attribute(Dim, ErrorBg),
        });

        // The status bar draws from the menu scheme: dim labels, accented keys.
        SchemeManager.AddScheme("Menu", new Scheme
        {
            Normal = new Attribute(Dim, Panel),
            HotNormal = new Attribute(Accent, Panel, TextStyle.Bold),
            Focus = new Attribute(White, SelectionBg),
            HotFocus = new Attribute(White, SelectionBg, TextStyle.Bold),
            Active = new Attribute(Text, Panel),
            HotActive = new Attribute(Accent, Panel, TextStyle.Bold),
            Highlight = new Attribute(White, SelectionBg),
            Disabled = new Attribute(Dim, Panel),
        });
    }

    /// <summary>A panel: card interior, accented rounded border and title.</summary>
    internal static void Panelise(View frame)
    {
        frame.BorderStyle = LineStyle.Rounded;
        frame.SetScheme(PanelScheme(focused: false));
    }

    /// <summary>Light the border up while this panel holds the keyboard. Without it, Tab moves focus
    /// invisibly and the screen looks like it is ignoring you.</summary>
    internal static void TrackFocus(View panel)
    {
        panel.HasFocusChanged += (_, _) => panel.SetScheme(PanelScheme(panel.HasFocus));
    }

    /// <summary>
    /// A Border is not a View here, so it has no scheme of its own - it draws in the owning frame's
    /// Normal. Hence Normal is the accent: it paints the border and title, while the background stays
    /// the panel colour and every child carries its own scheme for its text.
    /// </summary>
    private static Scheme PanelScheme(bool focused) => new()
    {
        Normal = focused ? new Attribute(White, Panel, TextStyle.Bold) : new Attribute(Accent, Panel),
        HotNormal = new Attribute(focused ? White : Accent, Panel, TextStyle.Bold),
        Focus = new Attribute(White, SelectionBg),
        HotFocus = new Attribute(White, SelectionBg, TextStyle.Bold),
        Highlight = new Attribute(White, SelectionBg),
        Editable = new Attribute(Text, Inset),
        ReadOnly = new Attribute(Dim, Panel),
        Disabled = new Attribute(Dim, Panel),
    };

    /// <summary>An action button in one of the accent colours; focus inverts to a filled block so the
    /// keyboard position is obvious without hunting for a cursor.</summary>
    internal static void Action(View button, TuiAccent accent)
    {
        Color colour = accent switch
        {
            TuiAccent.Read => Green,
            TuiAccent.Write => Amber,
            _ => Accent,
        };

        button.SetScheme(new Scheme
        {
            Normal = new Attribute(colour, Panel, TextStyle.Bold),
            HotNormal = new Attribute(colour, Panel, TextStyle.Bold | TextStyle.Underline),
            Focus = new Attribute(Bg, colour, TextStyle.Bold),
            HotFocus = new Attribute(Bg, colour, TextStyle.Bold | TextStyle.Underline),
            Active = new Attribute(Bg, colour, TextStyle.Bold),
            HotActive = new Attribute(Bg, colour, TextStyle.Bold),
            Highlight = new Attribute(Bg, colour, TextStyle.Bold),
            Disabled = new Attribute(Dim, Panel),
        });
    }

    /// <summary>The editable port box: inset background so it reads as somewhere to type.</summary>
    internal static void Input(View field) => field.SetScheme(new Scheme
    {
        Normal = new Attribute(Text, Inset),
        Focus = new Attribute(White, Inset),
        HotNormal = new Attribute(Accent, Inset),
        HotFocus = new Attribute(Accent, Inset, TextStyle.Bold),
        Editable = new Attribute(Text, Inset),
        ReadOnly = new Attribute(Dim, Inset),
        Highlight = new Attribute(White, SelectionBg),
        Disabled = new Attribute(Dim, Inset),
    });

    /// <summary>Secondary text: hints, detected-port lists, the log.</summary>
    internal static void Secondary(View view) => view.SetScheme(new Scheme
    {
        Normal = new Attribute(Dim, Panel),
        HotNormal = new Attribute(Accent, Panel),
        Focus = new Attribute(White, SelectionBg),
        HotFocus = new Attribute(White, SelectionBg, TextStyle.Bold),
        Highlight = new Attribute(White, SelectionBg),
        Disabled = new Attribute(Dim, Panel),
    });

    /// <summary>Body text on a panel, with the selected row picked out.</summary>
    internal static void Body(View view) => view.SetScheme(new Scheme
    {
        Normal = new Attribute(Text, Panel),
        HotNormal = new Attribute(Accent, Panel, TextStyle.Bold),
        Focus = new Attribute(White, SelectionBg),
        HotFocus = new Attribute(White, SelectionBg, TextStyle.Bold),
        Active = new Attribute(Accent, Panel),
        HotActive = new Attribute(Accent, Panel, TextStyle.Bold),
        Highlight = new Attribute(White, SelectionBg),
        Disabled = new Attribute(Dim, Panel),
    });

    /// <summary>Status text, tinted by what it is saying.</summary>
    internal static void Status(View label, bool loaded) => label.SetScheme(new Scheme
    {
        Normal = new Attribute(loaded ? Green : Dim, Panel),
        HotNormal = new Attribute(loaded ? Green : Dim, Panel),
        Disabled = new Attribute(Dim, Panel),
    });
}

/// <summary>Which accent an action button carries.</summary>
internal enum TuiAccent
{
    Neutral,
    Read,
    Write,
}
