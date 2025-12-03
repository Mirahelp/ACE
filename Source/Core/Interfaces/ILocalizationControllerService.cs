namespace AgentCommandEnvironment.Core.Interfaces
{
    public interface ILocalizationControllerService
    {
        String GetText(String domain, String key, String locale);
        String GetText(String key);
    }
}

