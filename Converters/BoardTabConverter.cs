using System;
using System.Globalization;
using System.Windows.Data;

namespace Sati.Converters
{
    // Bool<->BoardTab for the task-board pills, mirroring EnumToBoolConverter's
    // Convert direction but with a non-nullable, BoardTab-specific ConvertBack.
    // Kept separate so the NoteType-bound radios that depend on EnumToBoolConverter
    // are untouched. Convert: is SelectedTab this pill's tab? ConvertBack: a pill
    // turning on sets SelectedTab to its tab; a pill turning off returns DoNothing,
    // so only the newly-checked pill writes — the rest stay quiet.
    public class BoardTabConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value?.ToString() == parameter?.ToString();

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is true && parameter is not null)
                return Enum.Parse(typeof(BoardTab), parameter.ToString()!);
            return Binding.DoNothing;
        }
    }
}