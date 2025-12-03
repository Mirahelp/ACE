using AgentCommandEnvironment.Core.Models;
using System.IO;
using AgentCommandEnvironment.Core.Enums;

namespace AgentCommandEnvironment.Core.Services;

public sealed class WorkspaceStateTrackerService
{
    private readonly Object syncRoot = new Object();
    private readonly Dictionary<String, FileSignature> knownFiles = new Dictionary<String, FileSignature>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<String> ignoredDirectories = new HashSet<String>(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".svn",
        ".hg",
        ".vs",
        ".agent",
        "bin",
        "obj",
        "node_modules"
    };

    private readonly HashSet<String> ignoredExtensions = new HashSet<String>(StringComparer.OrdinalIgnoreCase)
    {
        ".dll",
        ".exe",
        ".pdb",
        ".tmp",
        ".log"
    };

    public void Reset(String? workspacePath)
    {
        lock (syncRoot)
        {
            knownFiles.Clear();
            if (String.IsNullOrWhiteSpace(workspacePath))
            {
                return;
            }

            try
            {
                String normalizedRoot = Path.GetFullPath(workspacePath);
                CaptureSnapshot(normalizedRoot, knownFiles);
            }
            catch
            {
                knownFiles.Clear();
            }
        }
    }

    public IReadOnlyList<WorkspaceFileChangeRecord> DetectChanges(String workspacePath)
    {
        List<WorkspaceFileChangeRecord> changes = new List<WorkspaceFileChangeRecord>();
        if (String.IsNullOrWhiteSpace(workspacePath))
        {
            return changes;
        }

        String normalizedRoot;
        try
        {
            normalizedRoot = Path.GetFullPath(workspacePath);
        }
        catch
        {
            return changes;
        }

        Dictionary<String, FileSignature> currentSnapshot = new Dictionary<String, FileSignature>(StringComparer.OrdinalIgnoreCase);

        lock (syncRoot)
        {
            try
            {
                CaptureSnapshot(normalizedRoot, currentSnapshot);
            }
            catch
            {
                return changes;
            }

            foreach (KeyValuePair<String, FileSignature> entry in currentSnapshot)
            {
                if (!knownFiles.TryGetValue(entry.Key, out FileSignature previousSignature))
                {
                    changes.Add(new WorkspaceFileChangeRecord
                    {
                        Kind = WorkspaceFileChangeOptions.Created,
                        RelativePath = entry.Key,
                        Current = entry.Value,
                        Previous = FileSignature.Empty
                    });
                    continue;
                }

                if (!AreSignaturesEquivalent(previousSignature, entry.Value))
                {
                    changes.Add(new WorkspaceFileChangeRecord
                    {
                        Kind = WorkspaceFileChangeOptions.Modified,
                        RelativePath = entry.Key,
                        Current = entry.Value,
                        Previous = previousSignature
                    });
                }
            }

            foreach (KeyValuePair<String, FileSignature> entry in knownFiles)
            {
                if (currentSnapshot.ContainsKey(entry.Key))
                {
                    continue;
                }

                changes.Add(new WorkspaceFileChangeRecord
                {
                    Kind = WorkspaceFileChangeOptions.Deleted,
                    RelativePath = entry.Key,
                    Previous = entry.Value,
                    Current = FileSignature.Empty
                });
            }

            knownFiles.Clear();
            foreach (KeyValuePair<String, FileSignature> entry in currentSnapshot)
            {
                knownFiles[entry.Key] = entry.Value;
            }
        }

        return changes;
    }

    private void CaptureSnapshot(String workspaceRoot, Dictionary<String, FileSignature> snapshot)
    {
        if (!Directory.Exists(workspaceRoot))
        {
            return;
        }

        Stack<String> pendingDirectories = new Stack<String>();
        pendingDirectories.Push(workspaceRoot);

        while (pendingDirectories.Count > 0)
        {
            String currentDirectory = pendingDirectories.Pop();

            IEnumerable<String> subdirectories;
            try
            {
                subdirectories = Directory.EnumerateDirectories(currentDirectory);
            }
            catch
            {
                continue;
            }

            foreach (String directory in subdirectories)
            {
                if (ShouldSkipDirectory(directory))
                {
                    continue;
                }

                pendingDirectories.Push(directory);
            }

            IEnumerable<String> files;
            try
            {
                files = Directory.EnumerateFiles(currentDirectory);
            }
            catch
            {
                continue;
            }

            foreach (String file in files)
            {
                if (ShouldSkipFile(file))
                {
                    continue;
                }

                FileInfo fileInfo;
                try
                {
                    fileInfo = new FileInfo(file);
                }
                catch
                {
                    continue;
                }

                String relativePath = Path.GetRelativePath(workspaceRoot, file);
                relativePath = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                snapshot[relativePath] = new FileSignature(fileInfo.Length, fileInfo.LastWriteTimeUtc);
            }
        }
    }

    private Boolean ShouldSkipDirectory(String directoryPath)
    {
        String directoryName = Path.GetFileName(directoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return ignoredDirectories.Contains(directoryName);
    }

    private Boolean ShouldSkipFile(String filePath)
    {
        String extension = Path.GetExtension(filePath);
        if (!String.IsNullOrWhiteSpace(extension) && ignoredExtensions.Contains(extension.ToLowerInvariant()))
        {
            return true;
        }

        return false;
    }

    private static Boolean AreSignaturesEquivalent(FileSignature left, FileSignature right)
    {
        if (left.Size != right.Size)
        {
            return false;
        }

        Double deltaSeconds = Math.Abs((left.LastWriteUtc - right.LastWriteUtc).TotalSeconds);
        return deltaSeconds < 0.5;
    }
}


