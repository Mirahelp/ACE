namespace AgentCommandEnvironment.Core.Models;

public readonly struct FileSignature
{
    public static FileSignature Empty { get; } = new FileSignature(-1, DateTime.MinValue);

    public FileSignature(Int64 size, DateTime lastWriteUtc)
    {
        Size = size;
        LastWriteUtc = lastWriteUtc;
    }

    public Int64 Size { get; }
    public DateTime LastWriteUtc { get; }
}

