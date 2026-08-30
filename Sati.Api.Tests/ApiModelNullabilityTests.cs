using Microsoft.EntityFrameworkCore;
using Sati.Api.Data;
using Xunit;

namespace Sati.Api.Tests;

/// <summary>
/// Guards the API persistence model against becoming looser than the schema it
/// reads and writes. A nullable API property backed by a NOT NULL column defers
/// the mistake until SaveChanges, where it becomes a database exception.
/// </summary>
public sealed class ApiModelNullabilityTests
{
    [Fact]
    public void RequiredPersonAndClaimValuesAreNotNullableInTheApiModel()
    {
        var options = new DbContextOptionsBuilder<ApiDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        using var db = new ApiDbContext(options);

        var requiredProperties = new[]
        {
            (Entity: typeof(ServerPerson), Property: nameof(ServerPerson.FirstName)),
            (Entity: typeof(ServerPerson), Property: nameof(ServerPerson.LastName)),
            (Entity: typeof(ServerClaimLine), Property: nameof(ServerClaimLine.Units))
        };

        var unexpectedlyNullable = requiredProperties
            .Where(required => db.Model.FindEntityType(required.Entity)!
                .FindProperty(required.Property)!
                .IsNullable)
            .Select(required => $"{required.Entity.Name}.{required.Property}")
            .ToList();

        Assert.Empty(unexpectedlyNullable);
    }
}
