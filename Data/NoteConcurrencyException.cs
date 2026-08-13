namespace Sati.Data;

public sealed class NoteConcurrencyException : Exception
{
    public NoteConcurrencyException()
        : base("This note changed after it was opened. Reload the saved copy before applying your changes.")
    {
    }

    public NoteConcurrencyException(Exception innerException)
        : base("This note changed after it was opened. Reload the saved copy before applying your changes.", innerException)
    {
    }
}
