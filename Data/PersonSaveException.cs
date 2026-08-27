using Sati.Contracts.V1;

namespace Sati.Data;

public sealed class PersonValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception(PersonSaveRules.Describe(errors))
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

public sealed class PersonPersistenceException(string message, Exception innerException)
    : Exception(message, innerException);
