using AgentCommandEnvironment.Core.Constants;
using AgentCommandEnvironment.Core.Interfaces;
using NGettext;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;

namespace AgentCommandEnvironment.Core.Controllers
{
    public sealed class GetTextLocalizationController : ILocalizationControllerService
    {
        private readonly ConcurrentDictionary<String, ICatalog?> catalogCache = new(StringComparer.Ordinal);

        public String GetText(String domain, String key, String locale)
        {
            if (String.IsNullOrWhiteSpace(key))
            {
                return String.Empty;
            }

            String effectiveDomain = String.IsNullOrWhiteSpace(domain) ? AppStrings.LocalizationDefaultDomain : domain;
            CultureInfo requestedCultureInfo = BuildCultureInfo(locale);
            ICatalog catalog = GetOrCreateCatalog(effectiveDomain, requestedCultureInfo);

            String translatedValue = catalog.GetString(key);
            if (String.IsNullOrEmpty(translatedValue))
            {
                return key;
            }

            return translatedValue;
        }

        public String GetText(String key)
        {
            return GetText(AppStrings.LocalizationDefaultDomain, key, AppStrings.LocalizationLocaleEnUs);
        }

        private ICatalog GetOrCreateCatalog(String domain, CultureInfo requestedCultureInfo)
        {
            CultureInfo effectiveCultureInfo = ResolveEffectiveCultureInfo(domain, requestedCultureInfo);
            String cacheKey = domain + "|" + effectiveCultureInfo.Name;

            if (!this.catalogCache.TryGetValue(cacheKey, out ICatalog? catalog) || catalog == null)
            {
                catalog = CreateCatalog(domain, effectiveCultureInfo);
                this.catalogCache[cacheKey] = catalog;
            }

            return catalog;
        }

        private CultureInfo ResolveEffectiveCultureInfo(String domain, CultureInfo requestedCultureInfo)
        {
            String normalizedRequestedFolderName = ConvertCultureInfoToUnderscoreName(requestedCultureInfo);
            String requestedMoFilePath = BuildMoFilePath(domain, normalizedRequestedFolderName);

            if (File.Exists(requestedMoFilePath))
            {
                return requestedCultureInfo;
            }

            CultureInfo fallbackCultureInfo = BuildCultureInfo(AppStrings.LocalizationLocaleEnUs);
            String normalizedFallbackFolderName = ConvertCultureInfoToUnderscoreName(fallbackCultureInfo);
            String fallbackMoFilePath = BuildMoFilePath(domain, normalizedFallbackFolderName);

            if (File.Exists(fallbackMoFilePath))
            {
                return fallbackCultureInfo;
            }

            return requestedCultureInfo;
        }

        private ICatalog CreateCatalog(String domain, CultureInfo cultureInfo)
        {
            String localesRootDirectoryPath = Path.Combine(AppContext.BaseDirectory, AppStrings.LocalizationFolderRoot);
            ICatalog catalog = new Catalog(domain, localesRootDirectoryPath, cultureInfo);
            return catalog;
        }

        private CultureInfo BuildCultureInfo(String locale)
        {
            if (String.IsNullOrWhiteSpace(locale))
            {
                return CultureInfo.CurrentUICulture;
            }

            String cultureName = locale.Replace('_', '-');
            return new CultureInfo(cultureName);
        }

        private String ConvertCultureInfoToUnderscoreName(CultureInfo cultureInfo)
        {
            return cultureInfo.Name.Replace('-', '_');
        }

        private String BuildMoFilePath(String domain, String normalizedLocaleFolderName)
        {
            String localesRootDirectoryPath = Path.Combine(AppContext.BaseDirectory, AppStrings.LocalizationFolderRoot);
            String moFileName = domain + ".mo";
            return Path.Combine(localesRootDirectoryPath, normalizedLocaleFolderName, AppStrings.LocalizationMessagesFolder, moFileName);
        }
    }
}

