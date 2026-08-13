namespace Sati.Data;

public sealed class AtRequestConcurrencyException : Exception
{
    public AtRequestConcurrencyException()
        : base("This AT request changed after it was opened. Reload the saved request before applying your changes.")
    {
    }

    public AtRequestConcurrencyException(Exception innerException)
        : base("This AT request changed after it was opened. Reload the saved request before applying your changes.", innerException)
    {
    }
}
