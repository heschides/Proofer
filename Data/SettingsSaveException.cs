namespace Sati.Data;

public sealed class SettingsSaveException : Exception
{
    public SettingsSaveException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
