namespace AgentCommandEnvironment.Core.Services;

public static class TextUtilityService
{
    public static String BuildCompactSnippet(String? sourceText, Int32 maxLength = 1200)
    {
        if (String.IsNullOrWhiteSpace(sourceText))
        {
            return String.Empty;
        }

        String normalized = sourceText.Trim();
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized.Substring(0, maxLength) + "...";
    }
}

