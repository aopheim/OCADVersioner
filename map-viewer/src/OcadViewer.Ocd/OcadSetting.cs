using System.Globalization;
using System.Text;

namespace OcadViewer.Ocd;

/// <summary>
/// One OCAD parameter string: colours, background maps, georeferencing, print
/// settings and the like. OCAD 9 and later keep all of these as tagged strings.
/// </summary>
/// <remarks>
/// A string is a first field followed by tab-separated fields, each led by a
/// one-letter code: <c>Black\tn0\tc0\tm0\ty0\tk100</c>. <see cref="SettingType"/>
/// names the types callers refer to.
/// </remarks>
public sealed class OcadSetting
{
    public int Type { get; }

    /// <summary>Whether the string belongs to the whole map rather than to one object.</summary>
    public bool IsMapWide => ObjectIndex == 0;

    public string Text => Encoding.Latin1.GetString(Content.Span);

    /// <summary>The text before the first tab, e.g. a colour's name.</summary>
    public string FirstField
    {
        get {
            string text = Text;
            int tab = text.IndexOf('\t');
            return tab < 0 ? text : text[..tab];
        }
    }

    /// <summary>The file the string was read from.</summary>
    internal OcadFile File { get; }

    /// <summary>The 0-based entry in the string index.</summary>
    internal int Entry { get; }

    /// <summary>The object entry the string belongs to, or 0 for map-wide settings.</summary>
    internal int ObjectIndex { get; }

    /// <summary>The string's characters, without the padding OCAD reserves after them.</summary>
    internal ReadOnlyMemory<byte> Content { get; }

    internal OcadSetting(OcadFile file, int entry, int type, int objectIndex, ReadOnlyMemory<byte> reserved)
    {
        File = file;
        Entry = entry;
        Type = type;
        ObjectIndex = objectIndex;
        int end = reserved.Span.IndexOf((byte)0);
        Content = end < 0 ? reserved : reserved[..end];
    }

    /// <summary>The value of the field led by <paramref name="code"/>, or null.</summary>
    public string Field(char code)
    {
        string[] parts = Text.Split('\t');
        for (int i = 1; i < parts.Length; i++) {
            if (parts[i].Length > 0 && parts[i][0] == code)
                return parts[i][1..];
        }
        return null;
    }

    public double? NumberField(char code)
        => double.TryParse(Field(code), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : null;

    /// <summary>Whether <paramref name="other"/> is the same type with exactly the same text.</summary>
    public bool SameContent(OcadSetting other)
        => Type == other.Type && Content.Span.SequenceEqual(other.Content.Span);

    public override string ToString() => $"{Type}: {Text}";
}
