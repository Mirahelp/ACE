using AgentCommandEnvironment.Core.Models;
using System.IO;
using System.Text;

namespace AgentCommandEnvironment.Core.Services;

public static class WorkspaceContextBuilderService
{
    public static Task<String?> BuildSummaryAsync(String workspacePath, IReadOnlyList<WorkspaceFileItem> files)
    {
        if (String.IsNullOrWhiteSpace(workspacePath) || files == null || files.Count == 0)
        {
            return Task.FromResult<String?>(null);
        }

        return Task.Run(() => BuildSummary(workspacePath, files));
    }

    private static String? BuildSummary(String workspacePath, IReadOnlyList<WorkspaceFileItem> files)
    {
        if (files.Count == 0)
        {
            return null;
        }

        String workspaceRootFullPath;
        try
        {
            workspaceRootFullPath = Path.GetFullPath(workspacePath);
        }
        catch
        {
            workspaceRootFullPath = workspacePath;
        }

        StringBuilder builder = new StringBuilder();
        builder.AppendLine("Workspace path: " + workspaceRootFullPath);
        builder.AppendLine("Context files with contents (truncated when needed):");

        Int32 maxFiles = 20;
        Int32 processedFiles = 0;
        Int32 maxCharactersPerFile = 4000;

        for (Int32 index = 0; index < files.Count && processedFiles < maxFiles; index++)
        {
            WorkspaceFileItem item = files[index];
            if (item == null)
            {
                continue;
            }

            String displayPath = !String.IsNullOrWhiteSpace(item.Path) ? item.Path : item.FullPath;
            builder.AppendLine();
            builder.AppendLine("File: " + displayPath);

            if (String.IsNullOrWhiteSpace(item.FullPath))
            {
                continue;
            }

            if (!File.Exists(item.FullPath))
            {
                continue;
            }

            String fileText;
            try
            {
                fileText = File.ReadAllText(item.FullPath, Encoding.UTF8);
            }
            catch
            {
                continue;
            }

            if (fileText.Length > maxCharactersPerFile)
            {
                fileText = fileText.Substring(0, maxCharactersPerFile);
            }

            builder.AppendLine("----- Start of file content (possibly truncated) -----");
            builder.AppendLine(fileText);
            builder.AppendLine("----- End of file content -----");

            processedFiles = processedFiles + 1;
        }

        return processedFiles == 0 ? null : builder.ToString();
    }
}

