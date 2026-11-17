using System.Reflection;

namespace OcadViewer.Ocd.Tests;

/// <summary>
/// What OcadViewer.Ocd shows the rest of the solution. Byte offsets, index blocks
/// and raw records stay inside it; making anything else public is a decision
/// about the boundary and has to be written down here.
/// </summary>
[TestFixture]
public sealed class PublicSurfaceTests
{
    private static readonly string[] PublicTypes = [
        nameof(Bounds),
        nameof(ObjectFields),
        nameof(ObjectStatus),
        nameof(ObjectType),
        nameof(OcadCoord),
        nameof(OcadEditor),
        nameof(OcadFile),
        nameof(OcadFormatException),
        nameof(OcadObject),
        nameof(OcadSetting),
        nameof(OcadSymbol),
        nameof(SettingType),
        nameof(SymbolFields),
    ];

    [Test]
    public void OnlyTheseTypesArePublic()
        => Assert.That(typeof(OcadFile).Assembly.GetExportedTypes().Select(t => t.Name).Order(),
                       Is.EqualTo(PublicTypes.Order()));

    [Test]
    public void NoPublicMemberHandsOutRawBytes()
    {
        Type[] bytes = [typeof(byte[]), typeof(ReadOnlyMemory<byte>), typeof(Memory<byte>)];
        string[] exposing = [.. typeof(OcadFile).Assembly.GetExportedTypes()
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Where(p => bytes.Contains(p.PropertyType))
            .Select(p => $"{p.DeclaringType.Name}.{p.Name}")];

        Assert.That(exposing, Is.Empty);
    }
}
