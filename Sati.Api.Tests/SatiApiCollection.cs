using Xunit;

namespace Sati.Api.Tests;

/// <summary>
/// Puts every API integration test class into one xUnit collection.
///
/// SatiApiFactory backs the API with a single NAMED shared in-memory SQLite
/// database ("Data Source=SatiApiTests;Mode=Memory;Cache=Shared"). The name is
/// what makes the connection poolable across the host and the test, and it is
/// also what makes it global: two class fixtures do not get two databases, they
/// get two seeders racing on one. xUnit parallelises across test classes by
/// default, so before this collection existed a second test class turned the
/// whole suite red in ways that vanished when either class ran alone.
///
/// A collection fixture also means the seed runs once rather than once per class.
/// Tests that create their own rows are responsible for removing them.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SatiApiCollection : ICollectionFixture<SatiApiFactory>
{
    public const string Name = "Sati API integration";
}
