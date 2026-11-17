namespace OcadViewer.Merge.Tests;

/// <summary>What the merge engine's writing shows the rest of the solution.</summary>
[TestFixture]
public sealed class PublicSurfaceTests
{
    [Test]
    public void OnlyTheWriterIsPublicForWriting()
        // MergeWriter refuses an incomplete merge; planning and splicing are its
        // internals, so nothing can save a merge by going around it.
        => Assert.That(typeof(Writing.MergeWriter).Assembly.GetExportedTypes()
                           .Where(t => t.Namespace == "OcadViewer.Merge.Writing")
                           .Select(t => t.Name),
                       Is.EqualTo(new[] { nameof(Writing.MergeWriter) }));
}
