using System.IO;
using System.Windows;

namespace Sati.Services
{
    public sealed record ThemeOption(string DisplayName, string ResourceName);

    /// <summary>
    /// Applies one of the app's interchangeable theme dictionaries and persists the
    /// choice per Windows user. Functional state colors remain in States.xaml.
    /// </summary>
    public sealed class ThemeService
    {
        private const string DefaultTheme = "SunlitShell";
        private readonly string _preferencePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Sati",
            "theme.txt");

        public IReadOnlyList<ThemeOption> Themes { get; } =
        [
            new("Sunlit Shell", "SunlitShell"),
            new("Apricot Pearl", "ApricotPearl"),
            new("Rosewater Pearl", "RosewaterPearl"),
            new("Pearlescent Cream", "PearlescentCream"),
            new("Lilly's Theme", "LillysTheme"),
            new("Woodfords", "Woodfords"),
            new("Warm Earth", "WarmEarth"),
            new("Moonlit Pearl", "MoonlitPearl"),
            new("Iridescent Jewel", "IridescentJewel"),
            new("Midnight Opal", "MidnightOpal")
        ];

        public ThemeOption CurrentTheme { get; private set; }
        public event EventHandler? ThemeChanged;

        public ThemeService()
        {
            var savedTheme = ReadSavedTheme();
            CurrentTheme = FindTheme(savedTheme) ?? FindTheme(DefaultTheme)!;
            ApplyTheme(CurrentTheme.ResourceName, persist: false);
        }

        public void ApplyTheme(string resourceName) => ApplyTheme(resourceName, persist: true);

        private void ApplyTheme(string resourceName, bool persist)
        {
            var theme = FindTheme(resourceName)
                ?? throw new ArgumentException($"Unknown theme '{resourceName}'.", nameof(resourceName));

            var dictionaries = Application.Current.Resources.MergedDictionaries;
            var currentDictionary = dictionaries.FirstOrDefault(IsThemeDictionary);
            var newDictionary = new ResourceDictionary
            {
                Source = new Uri($"/Themes/{theme.ResourceName}.xaml", UriKind.Relative)
            };

            if (currentDictionary is null)
                dictionaries.Add(newDictionary);
            else
                dictionaries[dictionaries.IndexOf(currentDictionary)] = newDictionary;

            CurrentTheme = theme;
            ThemeChanged?.Invoke(this, EventArgs.Empty);

            if (persist)
                SaveTheme(theme.ResourceName);
        }

        private bool IsThemeDictionary(ResourceDictionary dictionary)
        {
            var source = dictionary.Source?.OriginalString;
            return source is not null && Themes.Any(theme =>
                source.EndsWith($"/{theme.ResourceName}.xaml", StringComparison.OrdinalIgnoreCase)
                || source.Equals($"Themes/{theme.ResourceName}.xaml", StringComparison.OrdinalIgnoreCase));
        }

        private ThemeOption? FindTheme(string? resourceName) => Themes.FirstOrDefault(theme =>
            theme.ResourceName.Equals(resourceName, StringComparison.OrdinalIgnoreCase));

        private string? ReadSavedTheme()
        {
            try
            {
                return File.Exists(_preferencePath)
                    ? File.ReadAllText(_preferencePath).Trim()
                    : null;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        private void SaveTheme(string resourceName)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_preferencePath)!);
                File.WriteAllText(_preferencePath, resourceName);
            }
            catch (IOException)
            {
                // The preview still works if persistence is temporarily unavailable.
            }
            catch (UnauthorizedAccessException)
            {
                // The preview still works if persistence is unavailable for this user.
            }
        }
    }
}
