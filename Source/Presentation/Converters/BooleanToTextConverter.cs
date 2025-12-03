using Avalonia.Data;
using Avalonia.Data.Converters;
using System.Globalization;
using System;

namespace AgentCommandEnvironment.Presentation.Converters;

public sealed class BooleanToTextConverter : IValueConverter
{
    public String TrueText { get; set; } = "True";
    public String FalseText { get; set; } = "False";

    public Object? Convert(Object? value, Type targetType, Object? parameter, CultureInfo culture)
    {
        if (value is Boolean flag)
        {
            return flag ? TrueText : FalseText;
        }

        return FalseText;
    }

    public Object? ConvertBack(Object? value, Type targetType, Object? parameter, CultureInfo culture)
    {
        return BindingOperations.DoNothing;
    }
}
