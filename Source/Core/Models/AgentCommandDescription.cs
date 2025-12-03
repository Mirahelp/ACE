using System.Text.Json.Serialization;

namespace AgentCommandEnvironment.Core.Models;

public sealed class AgentCommandDescription
{
    [JsonPropertyName("id")]
    public String? Id { get; set; }

    [JsonPropertyName("description")]
    public String? Description { get; set; }

    [JsonPropertyName("executable")]
    public String? Executable { get; set; }

    [JsonPropertyName("arguments")]
    public String? Arguments { get; set; }

    [JsonPropertyName("workingDirectory")]
    public String? WorkingDirectory { get; set; }

    [JsonPropertyName("dangerLevel")]
    public String? DangerLevel { get; set; }

    [JsonPropertyName("expectedExitCode")]
    public Int32? ExpectedExitCode { get; set; }

    [JsonPropertyName("runInBackground")]
    public Boolean? RunInBackground { get; set; }

    [JsonPropertyName("maxRunSeconds")]
    public Int32? MaxRunSeconds { get; set; }
}

