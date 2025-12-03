using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace AgentCommandEnvironment.Presentation.Converters
{
    public sealed class BooleanToBrushConverter : IValueConverter
    {
        public IBrush? TrueBrush { get; set; }
        public IBrush? FalseBrush { get; set; }

        public Object? Convert(Object? value, Type targetType, Object? parameter, CultureInfo culture)
        {
            Boolean flag = value is Boolean boolean && boolean;
            return flag ? TrueBrush : FalseBrush;
        }

        public Object? ConvertBack(Object? value, Type targetType, Object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
