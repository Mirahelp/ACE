using System;
using System.Text.Json.Serialization;

namespace AgentCommandEnvironment.Core.Results;

public sealed class AnalystFactResult
{
    [JsonPropertyName("summary")]
    public String? Summary { get; set; }

    [JsonPropertyName("detail")]
    public String? Detail { get; set; }

    [JsonPropertyName("file")]
    public String? File { get; set; }

    [JsonPropertyName("key")]
    public String? LegacyKey { get; set; }

    [JsonPropertyName("value")]
    public String? LegacyValue { get; set; }

    public String? GetSummary()
    {
        return !String.IsNullOrWhiteSpace(Summary) ? Summary : LegacyKey;
    }

    public String? GetDetail()
    {
        return !String.IsNullOrWhiteSpace(Detail) ? Detail : LegacyValue;
    }
}
