using System;
using System.Globalization;
using System.Windows.Data;

namespace Alfresco.App.Converters
{
    /// <summary>
    /// Converter za prikaz akcije u DataGrid-u
    /// 0 = Bez promene, 1 = Aktiviraj, 2 = Deaktiviraj
    /// </summary>
    public class ActionToTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int action)
            {
                return action switch
                {
                    0 => "Bez promene",
                    1 => "Aktiviraj",
                    2 => "Deaktiviraj",
                    _ => action.ToString()
                };
            }

            return value?.ToString() ?? string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
