using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;
using AgentCommandEnvironment.Core.Enums;

namespace AgentCommandEnvironment.Presentation.Converters
{
    public sealed class SmartTaskStateToBrushConverter : IValueConverter
    {
        public IBrush PendingBrush { get; set; } = new SolidColorBrush(Color.Parse("#FF8E8E8E"));
        public IBrush PlanningBrush { get; set; } = new SolidColorBrush(Color.Parse("#FF0078D4"));
        public IBrush ExecutingBrush { get; set; } = new SolidColorBrush(Color.Parse("#FFF7630C"));
        public IBrush VerifyingBrush { get; set; } = new SolidColorBrush(Color.Parse("#FF9860E5"));
        public IBrush SucceededBrush { get; set; } = new SolidColorBrush(Color.Parse("#FF0F9D58"));
        public IBrush FailedBrush { get; set; } = new SolidColorBrush(Color.Parse("#FFD13438"));
        public IBrush SkippedBrush { get; set; } = new SolidColorBrush(Color.Parse("#FFB0B0B0"));
        public IBrush DefaultBrush { get; set; } = new SolidColorBrush(Color.Parse("#FFB0BEC5"));

        public Object? Convert(Object? value, Type targetType, Object? parameter, CultureInfo culture)
        {
            SmartTaskStateOptions? state = value switch
            {
                SmartTaskStateOptions typed => typed,
                String text when Enum.TryParse(text, true, out SmartTaskStateOptions parsed) => parsed,
                _ => null
            };

            if (!state.HasValue)
            {
                return DefaultBrush;
            }

            return state.Value switch
            {
                SmartTaskStateOptions.Pending => PendingBrush,
                SmartTaskStateOptions.Planning => PlanningBrush,
                SmartTaskStateOptions.Executing => ExecutingBrush,
                SmartTaskStateOptions.Verifying => VerifyingBrush,
                SmartTaskStateOptions.Succeeded => SucceededBrush,
                SmartTaskStateOptions.Failed => FailedBrush,
                SmartTaskStateOptions.Skipped => SkippedBrush,
                _ => DefaultBrush
            };
        }

        public Object? ConvertBack(Object? value, Type targetType, Object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}

