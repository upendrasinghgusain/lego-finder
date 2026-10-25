using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LegoFinder.Core.Contracts;

public static class Schemas
{
    public const string Parts = "legofinder.parts/1";
    public const string Scan = "legofinder.scan/1";
    public const string Find = "legofinder.find/1";
}

/// <summary>Serialisation shared by every pipeline stage, so their JSON always agrees.</summary>
public static class LegoJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        // Keep '×', '&' etc. readable in the output instead of \uXXXX escapes.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    /// <summary>Deserialises a pipeline document, rejecting anything with a different <c>schema</c>.</summary>
    public static T Deserialize<T>(string json, string expectedSchema)
    {
        string? schema;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
            schema = doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("schema", out var s)
                && s.ValueKind == JsonValueKind.String
                    ? s.GetString()
                    : null;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Input is not valid JSON: {ex.Message}", ex);
        }

        if (schema != expectedSchema)
        {
            throw new InvalidDataException(
                $"Expected a '{expectedSchema}' document but got '{schema ?? "(no schema field)"}'.");
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, Options)
                ?? throw new InvalidDataException("Input JSON is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Input does not match '{expectedSchema}': {ex.Message}", ex);
        }
    }
}
