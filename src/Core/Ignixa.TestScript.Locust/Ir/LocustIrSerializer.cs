using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ignixa.TestScript.Locust.Ir;

/// <summary>
/// Serializes a <see cref="LocustIrDocument"/> into the canonical JSON contract shared with the Python runtime.
/// </summary>
public static class LocustIrSerializer
{
    /// <summary>
    /// The version of the intermediate representation schema produced by this compiler.
    /// </summary>
    public const string SchemaVersion = "1.0";

    private static readonly JsonSerializerOptions s_options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>
    /// Serializes the given <see cref="LocustIrDocument"/> to its canonical JSON representation.
    /// </summary>
    /// <param name="document">The intermediate representation document to serialize.</param>
    /// <returns>The canonical JSON text for the document.</returns>
    public static string Serialize(LocustIrDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return JsonSerializer.Serialize(document, s_options);
    }
}
