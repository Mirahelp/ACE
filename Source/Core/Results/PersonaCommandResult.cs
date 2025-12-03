using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using AgentCommandEnvironment.Core.Models;

namespace AgentCommandEnvironment.Core.Results;

public sealed class PersonaCommandResult
{
    [JsonPropertyName("commands")]
    public List<AgentCommandDescription>? Commands { get; set; }

    [JsonPropertyName("notes")]
    public String? Notes { get; set; }
}
