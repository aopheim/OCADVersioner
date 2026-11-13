using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OcadViewer.Versioning;

/// <summary>
/// How this app writes JSON that the Angular app also reads.
/// </summary>
/// <remarks>
/// One definition, used for both the release metadata on disk and the settings in
/// local storage. It was two identical copies, one per service, which is the
/// cheapest possible way to let two file formats drift apart.
/// </remarks>
public static class AngularJson
{
    /// <summary>camelCase, nulls omitted, timestamps as the browser writes them.</summary>
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new BrowserTimestampConverter());
        return options;
    }
}

/// <summary>
/// Reads and writes timestamps the way <c>Date.toISOString()</c> does.
/// </summary>
/// <remarks>
/// .NET writes a <see cref="DateTimeOffset"/> as <c>2026-11-13T21:05:00.0000000+00:00</c>;
/// the browser writes <c>2026-11-13T21:05:00.000Z</c>. Both are valid ISO-8601 and
/// both parse, so nothing breaks - but a user comparing two release folders sees
/// one app's files written differently from the other's, and anything downstream
/// that does a string comparison on the field is quietly wrong. The shape of these
/// files is a contract, so this app writes the same bytes the Angular app does.
/// </remarks>
public sealed class BrowserTimestampConverter : JsonConverter<DateTimeOffset>
{
    private const string Format = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetDateTimeOffset();

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
        => writer.WriteStringValue(
            value.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture));
}
