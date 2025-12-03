using System;
using AgentCommandEnvironment.Core;
using AgentCommandEnvironment.Core.Interfaces;
using Avalonia.Markup.Xaml;

namespace AgentCommandEnvironment.Presentation.Localization
{
    public sealed class GetTextExtension : MarkupExtension
    {
        public String? Key { get; set; }

        public override Object ProvideValue(IServiceProvider serviceProvider)
        {
            if (String.IsNullOrWhiteSpace(Key))
            {
                return String.Empty;
            }

            ILocalizationControllerService localization = AppHost.Localization;
            String? translated = localization != null ? localization.GetText(Key) : Key;
            if (String.IsNullOrWhiteSpace(translated))
            {
                return Key;
            }

            return translated;
        }
    }
}
