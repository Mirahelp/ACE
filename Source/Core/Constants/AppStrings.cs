namespace AgentCommandEnvironment.Core.Constants
{
    public static class AppStrings
    {
        public const String LocalizationFolderRoot = "Locales";
        public const String LocalizationMessagesFolder = "LC_MESSAGES";
        public const String LocalizationDefaultDomain = "ui";
        public const String LocalizationLocaleEnUs = "en_US";

        public static String NormalizeLocale(String cultureName)
        {
            if (String.IsNullOrWhiteSpace(cultureName))
            {
                return LocalizationLocaleEnUs;
            }

            return cultureName.Replace('-', '_');
        }
    }
}

