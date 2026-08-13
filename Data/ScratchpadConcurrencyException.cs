namespace Sati.Data;

public sealed class ScratchpadConcurrencyException : Exception
{
    public ScratchpadConcurrencyException()
        : base("Your scratchpad was changed in another Sati session. Your text is still here, but it was not saved over the newer copy.")
    {
    }

    public ScratchpadConcurrencyException(Exception innerException)
        : base("Your scratchpad was changed in another Sati session. Your text is still here, but it was not saved over the newer copy.", innerException)
    {
    }
}
