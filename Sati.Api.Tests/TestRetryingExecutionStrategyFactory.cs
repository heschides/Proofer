using Microsoft.EntityFrameworkCore.Storage;

namespace Sati.Api.Tests;

// Exercise EF's same ambient-transaction guard as production EnableRetryOnFailure,
// without needing SQL Server or retrying deterministic SQLite test failures.
internal sealed class TestRetryingExecutionStrategyFactory(ExecutionStrategyDependencies dependencies) : IExecutionStrategyFactory
{
    public IExecutionStrategy Create() => new TestStrategy(dependencies);
    private sealed class TestStrategy(ExecutionStrategyDependencies dependencies) : ExecutionStrategy(dependencies, 1, TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => false;
    }
}
