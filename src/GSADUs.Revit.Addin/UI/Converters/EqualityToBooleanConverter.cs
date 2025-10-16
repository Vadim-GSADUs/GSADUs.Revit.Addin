using System;
using System.Globalization;
using System.Windows.Data;

namespace GSADUs.Revit.Addin.UI
{
    // Converts value.Equals(parameter) <-> bool
    // Forward: returns true when bound value equals ConverterParameter (case-insensitive for strings)
    // Backward: when true, returns parameter; when false, returns Binding.DoNothing
    internal sealed class EqualityToBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (parameter == null) return false;
            if (value == null) return false;

            if (value is string vs && parameter is string ps)
            {
                return string.Equals(vs, ps, StringComparison.OrdinalIgnoreCase);
            }
            return Equals(value, parameter);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                bool isChecked = value is bool b && b;
                if (!isChecked) return Binding.DoNothing;
                return parameter ?? Binding.DoNothing;
            }
            catch { return Binding.DoNothing; }
        }
    }
}
