using System.Diagnostics;
using Terminal.Gui.App;

namespace M0LTE.Tait.Codeplug.Cli;

/// <summary>
/// Times what one screen repaint costs on the console this is actually running on.
///
/// Terminal.Gui repaints the whole screen for every character typed into a text box, so a repaint IS
/// a keystroke as far as the editor is concerned. How long that takes is entirely down to the console
/// and the driver in front of it, and it is not something that can be measured from anywhere else -
/// hence a benchmark that ships in the tool rather than a number quoted from someone else's machine.
///
/// Run it per driver to find the quickest one for your console:
///   tait-codeplug tui --bench
///   tait-codeplug tui --bench --driver ansi
///   tait-codeplug tui --bench --driver windows
/// </summary>
internal static class TuiBench
{
    private const int WarmUp = 5;
    private const int Iterations = 30;

    internal static int Run(string? driver, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var timings = new List<double>(Iterations);
        int rows;
        int columns;
        string driverInUse;

        IApplication app = Application.Create();
        try
        {
            if (driver is not null)
            {
                app.ForceDriver = driver;
            }

            app.Init();

            using var window = BenchWindow(out Action mutate);
            int screenRows = 0;
            int screenColumns = 0;
            string seenDriver = "(unknown)";

            // The timing has to happen inside the running loop: outside it the driver is not live, the
            // terminal has not answered the size query yet, and a draw costs nothing because nothing
            // reaches the console. So run the app, do the work on the first tick, and stop.
            app.AddTimeout(TimeSpan.FromMilliseconds(250), () =>
            {
                seenDriver = driver ?? Terminal.Gui.Drivers.DriverRegistry.GetDefaultDriver().Name;
                screenRows = app.Screen.Height;
                screenColumns = app.Screen.Width;

                for (int i = 0; i < WarmUp + Iterations; i++)
                {
                    mutate();
                    long start = Stopwatch.GetTimestamp();
                    app.LayoutAndDraw(true);
                    double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    if (i >= WarmUp)
                    {
                        timings.Add(ms);
                    }
                }

                app.RequestStop(window);
                return false;
            });

            app.Run(window);
            rows = screenRows;
            columns = screenColumns;
            driverInUse = seenDriver;
        }
        finally
        {
            app.Dispose();
        }

        if (timings.Count == 0)
        {
            output.WriteLine("the benchmark did not get a chance to draw anything - is this a real terminal?");
            return 1;
        }

        timings.Sort();
        double median = timings[timings.Count / 2];
        double worst = timings[^1];
        double best = timings[0];
        int cells = rows * columns;

        output.WriteLine();
        output.WriteLine(FormattableString.Invariant($"screen        : {columns}x{rows} ({cells} cells)"));
        output.WriteLine(FormattableString.Invariant($"driver        : {driverInUse.ToLowerInvariant()}{(driver is null ? " (the default on this platform)" : " (forced)")}"));
        output.WriteLine(FormattableString.Invariant($"repaints      : {Iterations} timed, {WarmUp} discarded as warm-up"));
        output.WriteLine(FormattableString.Invariant($"per repaint   : {median:F1} ms median   ({best:F1} best, {worst:F1} worst)"));
        output.WriteLine();
        output.WriteLine("A repaint is what one character typed into a text box costs, because Terminal.Gui");
        output.WriteLine("redraws the whole screen for it. Under about 30ms feels instant; a few hundred");
        output.WriteLine("milliseconds is the editor feeling sluggish. Try --driver <name> to compare, and");
        output.WriteLine("a smaller window: the cost scales with the number of cells on screen.");
        return 0;
    }

    /// <summary>
    /// A window the size of the real one, with something on it that changes every repaint, so the
    /// driver has actual work to do rather than an unchanged screen.
    /// </summary>
    private static Terminal.Gui.Views.Window BenchWindow(out Action mutate)
    {
        var window = new Terminal.Gui.Views.Window { Title = "tait-codeplug repaint benchmark" };
        var label = new Terminal.Gui.Views.Label
        {
            X = 2,
            Y = 2,
            Text = "measuring what one screen repaint costs on this console...",
        };
        window.Add(label);

        int n = 0;
        mutate = () =>
        {
            n++;
            label.Text = $"measuring what one screen repaint costs on this console... {n}";
            window.SetNeedsDraw();
        };

        return window;
    }
}
