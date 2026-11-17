using OcadViewer.Merge.Analysis;

namespace OcadViewer.Wasm.Services;

/// <summary>Whose a change is, for its colour: one copy's, both copies', or a conflict.</summary>
public enum ChangeSide { A, B, Both, Conflict }

/// <summary>
/// The one mapping from a merge item to whose change it is, and from that to the
/// name the side panel's CSS and mergeOverlay.js style it by.
/// </summary>
public static class ChangeSides
{
    /// <summary>The side a clean change came from; a conflict is <see cref="ChangeSide.Conflict"/>.</summary>
    public static ChangeSide Of(MergeItem item) => item.Outcome switch {
        Outcome.OnlyA => ChangeSide.A,
        Outcome.OnlyB => ChangeSide.B,
        Outcome.BothSame => ChangeSide.Both,
        _ => ChangeSide.Conflict,
    };

    public static ChangeSide Of(Side side) => side == Side.A ? ChangeSide.A : ChangeSide.B;

    /// <summary>"a", "b", "both" or "conflict".</summary>
    public static string Name(this ChangeSide side) => side switch {
        ChangeSide.A => "a",
        ChangeSide.B => "b",
        ChangeSide.Both => "both",
        _ => "conflict",
    };
}
