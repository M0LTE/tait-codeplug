namespace M0LTE.Tait.Codeplug;

/// <summary>
/// What a long radio operation is doing, so a caller can show it rather than leave the operator
/// watching a frozen screen. A read is ~25 seconds and a write is comparable, both preceded by a
/// connect that can sit for up to 90 seconds waiting for the operator to power-cycle the radio.
/// </summary>
/// <param name="Phase">Which stage of the operation this is.</param>
/// <param name="Done">Units finished: sections read, or records written.</param>
/// <param name="Total">Units expected, or 0 when the total is not known yet.</param>
/// <param name="What">A short human-readable note, e.g. "section 05" or "record 412 of 1103".</param>
public sealed record ProgrammerProgress(ProgrammerPhase Phase, int Done, int Total, string What)
{
    /// <summary>Fraction complete in 0..1, or null when <see cref="Total"/> is not yet known.</summary>
    public double? Fraction => Total > 0 ? Math.Clamp((double)Done / Total, 0, 1) : null;
}

/// <summary>The stages a caller may want to show differently.</summary>
public enum ProgrammerPhase
{
    /// <summary>Probing for the radio's boot banner. This is where the operator must power-cycle.</summary>
    WaitingForRadio,

    /// <summary>The radio answered and programming mode is latched.</summary>
    Connected,

    /// <summary>Reading sections.</summary>
    Reading,

    /// <summary>The preamble reads and guards the CPS performs before a write block.</summary>
    PreparingWrite,

    /// <summary>Writing records.</summary>
    Writing,

    /// <summary>The write block has been committed.</summary>
    Committed,
}
