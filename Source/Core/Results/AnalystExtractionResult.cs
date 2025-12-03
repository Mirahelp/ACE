using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AgentCommandEnvironment.Core.Results;

public sealed class AnalystExtractionResult
{
    [JsonPropertyName("facts")]
    public List<AnalystFactResult>? Facts { get; set; }

    [JsonPropertyName("summary")]
    public String? Summary { get; set; }
}
