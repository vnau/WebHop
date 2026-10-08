using System.Text.Json.Serialization;

namespace WebHop.CLI
{
    /// <summary>
    /// Source-generated JSON for the CLI's own types. A trimmed single-file webhop.exe
    /// (PublishTrimmed) disables reflection-based serialization, so every JsonSerializer call goes through here.
    /// </summary>
    [JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonSerializable(typeof(CliConfig))]
    internal sealed partial class CliJsonContext : JsonSerializerContext;

    /// <summary>One --log-format=json line, with ngrok's field names; on one line and keeping a null err.</summary>
    internal sealed record LogLine(
        [property: JsonPropertyName("t")] string Time,
        [property: JsonPropertyName("lvl")] string Level,
        [property: JsonPropertyName("obj")] string Category,
        [property: JsonPropertyName("msg")] string Message,
        [property: JsonPropertyName("err")] string? Error);

    [JsonSerializable(typeof(LogLine))]
    internal sealed partial class LogLineJsonContext : JsonSerializerContext;
}
