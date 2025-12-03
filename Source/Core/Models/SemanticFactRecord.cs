using System.Globalization;
using AgentCommandEnvironment.Core.Enums;

namespace AgentCommandEnvironment.Core.Models;

public sealed class SemanticFactRecord
{
    public String Summary { get; set; } = String.Empty;
    public String Detail { get; set; } = String.Empty;
    public String FilePath { get; set; } = String.Empty;
    public String Source { get; set; } = String.Empty;
    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;
    public SemanticFactOptions Kind { get; set; } = SemanticFactOptions.General;

    public String File
    {
        get { return String.IsNullOrWhiteSpace(FilePath) ? "(workspace)" : FilePath; }
    }

    public String RecordedAtDisplay
    {
        get { return RecordedAtUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture); }
    }

    internal SemanticFactRecord Clone()
    {
        return new SemanticFactRecord
        {
            Summary = Summary,
            Detail = Detail,
            FilePath = FilePath,
            Source = Source,
            RecordedAtUtc = RecordedAtUtc,
            Kind = Kind
        };
    }
}


