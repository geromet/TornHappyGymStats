namespace HappyGymStats.Blazor.Components.Shared;

/// <summary>
/// How much weight a number on the board can bear (UX plan U001, docs/UX-PLAN.md).
///
/// The test that separates these is not how much arithmetic produced the number.
/// It is: <b>would this change if we polled again right now with nothing else
/// having happened?</b>
///
/// Coverage ratio is <c>openTargets / availableMembers</c> — arithmetic over two
/// measured values — and it is <see cref="Measured"/>, because polling again
/// gives the same answer. Score rate is arithmetic over two measured samples and
/// is <see cref="Projected"/>, because the window is part of the answer.
/// </summary>
public enum FigureKind
{
    /// <summary>Poll again, same answer. Torn told us, or it is arithmetic over
    /// values Torn told us that does not depend on time.</summary>
    Measured,

    /// <summary>Depends on a window, so the same war state yields a different
    /// number after a lull. True now, not true in five minutes.</summary>
    Projected,

    /// <summary>Derived from a proxy for the thing we actually want. More polling
    /// will not improve it; better data — a linked key — might.</summary>
    Inferred,
}

/// <summary>
/// The single place the vocabulary is written down. Panels must not invent their
/// own words for these; <c>scripts/verify/u001-honest-signal.sh</c> pins it.
/// </summary>
public static class FigureKinds
{
    public static string Label(FigureKind kind) => kind switch
    {
        FigureKind.Measured => "measured",
        FigureKind.Projected => "projected",
        FigureKind.Inferred => "inferred",
        _ => "unknown",
    };

    /// <summary>
    /// What the marker means, in words a person reads on a war night rather than
    /// the operator diagnostics the DTOs carry. The raw <c>Diagnostic</c> string
    /// stays available underneath, but it was written for a log, not a screen.
    /// </summary>
    public static string Explanation(FigureKind kind) => kind switch
    {
        FigureKind.Measured => "Reported by Torn. Polling again gives the same answer.",
        FigureKind.Projected => "Extrapolated from a recent window. The same war state gives a different number after a lull — plan with it, do not count on it.",
        FigureKind.Inferred => "Worked out from an indirect signal, not observed. It can be wrong in a way more polling will not fix.",
        _ => string.Empty,
    };

    /// <summary>
    /// Measured figures carry no marker: marking everything is the same as
    /// marking nothing, and the default a reader assumes is "this is a fact".
    /// Only the two kinds that are weaker than that announce themselves.
    /// </summary>
    public static bool NeedsMarker(FigureKind kind) => kind is FigureKind.Projected or FigureKind.Inferred;
}
