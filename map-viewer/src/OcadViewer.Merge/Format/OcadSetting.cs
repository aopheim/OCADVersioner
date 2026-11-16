using System.Globalization;
using System.Text;

namespace OcadViewer.Merge.Format;

/// <summary>
/// One OCAD parameter string: colours, background maps, georeferencing, print
/// settings and the like. OCAD 9 and later keep all of these as tagged strings.
/// </summary>
/// <remarks>
/// A string is a first field followed by tab-separated fields, each led by a
/// one-letter code: <c>Black\tn0\tc0\tm0\ty0\tk100</c>.
/// </remarks>
public sealed class OcadSetting
{
    /// <summary>OCAD's string type for a colour.</summary>
    public const int ColorType = 9;
    /// <summary>OCAD's string type for a background map (template).</summary>
    public const int TemplateType = 8;
    /// <summary>OCAD's string type for the scale and georeferencing parameters.</summary>
    public const int ScaleType = 1039;

    /// <summary>The 0-based entry in the string index.</summary>
    public int Slot { get; }

    public int Type { get; }

    /// <summary>The object slot the string belongs to, or 0 for map-wide settings.</summary>
    public int ObjectIndex { get; }

    /// <summary>The string's characters, without the padding OCAD reserves after them.</summary>
    public ReadOnlyMemory<byte> Content { get; }

    internal OcadSetting(int slot, int type, int objectIndex, ReadOnlyMemory<byte> reserved)
    {
        Slot = slot;
        Type = type;
        ObjectIndex = objectIndex;
        int end = reserved.Span.IndexOf((byte)0);
        Content = end < 0 ? reserved : reserved[..end];
    }

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

    public override string ToString() => $"{Type}: {Text}";
}
