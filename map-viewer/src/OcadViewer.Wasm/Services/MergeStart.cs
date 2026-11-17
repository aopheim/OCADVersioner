namespace OcadViewer.Wasm.Services;

/// <summary>The three files of a merge and the names the copies are shown under.</summary>
/// <param name="Resume">Saved progress to pick up, or null to start fresh.</param>
public sealed record MergeStart(MergeInput Base, MergeInput A, MergeInput B, string LabelA, string LabelB,
                                MergeProgress Resume = null);
