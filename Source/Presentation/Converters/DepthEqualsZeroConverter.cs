using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace AgentCommandEnvironment.Presentation.Converters;

public sealed class DepthEqualsZeroConverter : IValueConverter
{
    public Object? Convert(Object? value, Type targetType, Object? parameter, CultureInfo culture)
    {
        if (value is Int32 depth)
        {
            return depth <= 0;
        }

        if (value is IConvertible convertible)
        {
            return convertible.ToInt32(CultureInfo.InvariantCulture) <= 0;
        }

        return false;
    }

    public Object? ConvertBack(Object? value, Type targetType, Object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
