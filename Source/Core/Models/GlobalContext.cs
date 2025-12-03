using AgentCommandEnvironment.Core.Enums;
namespace AgentCommandEnvironment.Core.Models;

public sealed class GlobalContext
{
    private const Int32 MaxSemanticFacts = 4096;
    private readonly Object syncRoot = new Object();
    private readonly List<SemanticFactRecord> facts;
    private readonly HashSet<String> completedIntentHashes;
    private readonly List<CompletedIntentRecord> completedIntentLedger;

    public GlobalContext()
    {
        facts = new List<SemanticFactRecord>();
        completedIntentHashes = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
        completedIntentLedger = new List<CompletedIntentRecord>();
    }

    public SecurityProfile SecurityProfile { get; set; } = new SecurityProfile();

    public IReadOnlyList<SemanticFactRecord> Facts
    {
        get
        {
            lock (syncRoot)
            {
                List<SemanticFactRecord> snapshot = new List<SemanticFactRecord>(facts.Count);
                for (Int32 index = 0; index < facts.Count; index++)
                {
                    snapshot.Add(facts[index].Clone());
                }
                return snapshot;
            }
        }
    }

    public Boolean TryGetFact(String summary, out String value)
    {
        lock (syncRoot)
        {
            for (Int32 index = facts.Count - 1; index >= 0; index--)
            {
                SemanticFactRecord record = facts[index];
                if (String.Equals(record.Summary, summary, StringComparison.OrdinalIgnoreCase))
                {
                    value = record.Detail;
                    return true;
                }
            }
        }

        value = String.Empty;
        return false;
    }

    public void SetFact(String summary, String detail, String? source = null, String? filePath = null, SemanticFactOptions kind = SemanticFactOptions.General)
    {
        if (String.IsNullOrWhiteSpace(summary))
        {
            return;
        }

        SemanticFactRecord record = new SemanticFactRecord
        {
            Summary = summary.Trim(),
            Detail = detail?.Trim() ?? String.Empty,
            Source = source ?? String.Empty,
            FilePath = filePath?.Trim() ?? String.Empty,
            RecordedAtUtc = DateTime.UtcNow,
            Kind = kind
        };

        lock (syncRoot)
        {
            Int32 existingIndex = FindFactIndex(record.Summary, record.FilePath);
            if (existingIndex >= 0)
            {
                facts.RemoveAt(existingIndex);
                facts.Add(record);
            }
            else
            {
                facts.Add(record);
                if (facts.Count > MaxSemanticFacts)
                {
                    facts.RemoveAt(0);
                }
            }
        }
    }

    public Boolean HasCompletedIntent(String hash)
    {
        lock (syncRoot)
        {
            return completedIntentHashes.Contains(hash);
        }
    }

    public void RegisterCompletedIntent(String hash, String intent)
    {
        lock (syncRoot)
        {
            if (!completedIntentHashes.Contains(hash))
            {
                completedIntentHashes.Add(hash);
                completedIntentLedger.Add(new CompletedIntentRecord
                {
                    Hash = hash,
                    Intent = intent,
                    CompletedAtUtc = DateTime.UtcNow
                });
            }
        }
    }

    public IReadOnlyList<CompletedIntentRecord> CompletedIntentLedger
    {
        get
        {
            lock (syncRoot)
            {
                return new List<CompletedIntentRecord>(completedIntentLedger);
            }
        }
    }

    public void ResetState()
    {
        lock (syncRoot)
        {
            facts.Clear();
            completedIntentHashes.Clear();
            completedIntentLedger.Clear();
        }
    }

    private Int32 FindFactIndex(String summary, String filePath)
    {
        for (Int32 index = 0; index < facts.Count; index++)
        {
            SemanticFactRecord fact = facts[index];
            Boolean summaryMatches = String.Equals(fact.Summary, summary, StringComparison.OrdinalIgnoreCase);
            Boolean fileMatches = String.Equals(fact.FilePath, filePath, StringComparison.OrdinalIgnoreCase);
            if (summaryMatches && fileMatches)
            {
                return index;
            }
        }

        return -1;
    }
}


