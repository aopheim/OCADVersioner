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
        // Bytes go in (OcadFile.Read) and the whole file comes out (OcadEditor.ToArray);
        // no member hands out part of a file.
        string[] allowed = [$"{nameof(OcadEditor)}.{nameof(OcadEditor.ToArray)}"];
        Type[] bytes = [typeof(byte[]), typeof(ReadOnlyMemory<byte>), typeof(Memory<byte>),
                        typeof(ReadOnlySpan<byte>), typeof(Span<byte>), typeof(IEnumerable<byte>), typeof(IReadOnlyList<byte>)];
        const BindingFlags Visible = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        string[] exposing = [.. typeof(OcadFile).Assembly.GetExportedTypes()
            .SelectMany(t => t.GetProperties(Visible).Select(p => (Member: (MemberInfo)p, Type: p.PropertyType))
                .Concat(t.GetMethods(Visible).Select(m => (Member: (MemberInfo)m, Type: m.ReturnType)))
                .Concat(t.GetFields(Visible).Select(f => (Member: (MemberInfo)f, Type: f.FieldType))))
            .Where(m => bytes.Contains(m.Type))
            .Select(m => $"{m.Member.DeclaringType.Name}.{m.Member.Name}")
            .Except(allowed)];

        Assert.That(exposing, Is.Empty);
    }

    [Test]
    public void ObjectsCannotBeMovedFromOutside()
        // The index keeps an extent that includes the symbol size, which a move cannot
        // recompute; a moved object written to a file would carry a stale extent.
        => Assert.That(typeof(OcadObject).GetMethod(nameof(OcadObject.WithCoords), BindingFlags.Public | BindingFlags.Instance), Is.Null);
}
