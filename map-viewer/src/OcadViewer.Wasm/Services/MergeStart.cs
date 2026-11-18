namespace OcadViewer.Wasm.Services;

/// <summary>The three files of a merge and the names the copies are shown under.</summary>
public sealed record MergeStart(MergeInput Base, MergeInput A, MergeInput B, string LabelA, string LabelB);
