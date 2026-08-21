namespace M0LTE.Tait.Codeplug.Cli;

/// <summary>
/// How fast the interactive mode's main loop should tick, given how long it is since anyone touched
/// the keyboard or mouse.
///
/// Terminal.Gui iterates its main loop ~25 times a second whether or not anything has changed, and
/// every iteration rewrites cursor state whether or not the cursor moved. Measured on an idle TUI
/// with nothing happening at all: ~318 bytes/second of escape sequences, in 25 separate writes per
/// second, for as long as the tool is open. Locally that costs nothing you would notice. Over SSH it
/// is 25 packets a second the far end must keep servicing for ever, and a link that is congested or
/// stalls never gets a quiet moment to catch up.
///
/// So the loop runs at the library's normal rate while the tool is being used, and steps down when it
/// is left alone. Stepping down in two stages keeps the cost off the cases that would be noticed: a
/// pause to think does not slow anything perceptibly, and only walking away drops it right down.
///
///   touched within 10s   library rate (25/s)   ~315 bytes/sec   typing unaffected
///   10s to 60s           10/s                  ~128 bytes/sec   waking key measured at 45ms
///   over 60s             4/s                    ~54 bytes/sec   waking key measured at 60-248ms
///
/// The first keypress after a pause pays that once, and restores the fast rate for everything after
/// it. Ten seconds is far longer than any pause in typing, so the cost never lands on a burst of
/// keystrokes.
/// </summary>
internal static class TuiIdlePolicy
{
    /// <summary>A pause long enough that a slightly slower loop will not be felt.</summary>
    internal static readonly TimeSpan PausedAfter = TimeSpan.FromSeconds(10);

    /// <summary>Long enough that whoever started the tool has walked away from it.</summary>
    internal static readonly TimeSpan AwayAfter = TimeSpan.FromSeconds(60);

    /// <summary>Iterations per second during a pause: 100ms worst case, which reads as instant.</summary>
    internal const ushort PausedIterationsPerSecond = 10;

    /// <summary>Iterations per second once nobody is there. 250ms worst case on the key that wakes it.</summary>
    internal const ushort AwayIterationsPerSecond = 4;

    /// <summary>
    /// The rate to run at. <paramref name="activeRate"/> is whatever the library was configured with at
    /// startup, so this never invents a rate of its own for the active case, and never speeds the loop
    /// up beyond what the library asked for.
    /// </summary>
    internal static ushort RateFor(TimeSpan sinceLastInput, ushort activeRate)
    {
        if (sinceLastInput >= AwayAfter)
        {
            return Math.Min(AwayIterationsPerSecond, activeRate);
        }

        if (sinceLastInput >= PausedAfter)
        {
            return Math.Min(PausedIterationsPerSecond, activeRate);
        }

        return activeRate;
    }
}
