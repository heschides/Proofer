namespace Sati.Data;

public sealed class SettingsConcurrencyException : Exception
{
    public SettingsConcurrencyException()
        : base("Agency settings changed after this window was opened. Your changes were not saved; review the latest settings before trying again.")
    {
    }

    public SettingsConcurrencyException(Exception innerException)
        : base("Agency settings changed after this window was opened. Your changes were not saved; review the latest settings before trying again.", innerException)
    {
    }
}
