using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace Sati.Converters
{
    /// <summary>
    /// Turns stored PNG bytes into something an Image control can show.
    ///
    /// This exists so view models can hold <c>byte[]</c> rather than
    /// <c>BitmapImage</c>. AT request item screenshots are edited by view models
    /// that are also exercised by tests and used on the API-backed path, and
    /// neither should have to load WPF imaging to hold a picture.
    /// </summary>
    public sealed class PngBytesToImageConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not byte[] { Length: > 0 } bytes)
                return null;

            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                // CacheOption OnLoad reads the whole stream during EndInit so the
                // stream can be disposed immediately; the default would keep a
                // lazy reference to a stream this method is about to close.
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                image.StreamSource = new MemoryStream(bytes);
                image.EndInit();
                // Frozen so the bitmap can cross threads and so WPF stops tracking
                // it for changes — these are immutable once pasted.
                image.Freeze();
                return image;
            }
            catch (Exception)
            {
                // Stored bytes that will not decode are a data problem, not a
                // reason to bring down the editor. The control simply shows
                // nothing, and the "no screenshot" affordance remains visible.
                return null;
            }
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException("Screenshot bytes are written by the paste handler, not by a binding.");
    }
}
