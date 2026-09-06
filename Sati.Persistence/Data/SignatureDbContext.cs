using Microsoft.EntityFrameworkCore;
using Sati.Models;

namespace Sati.Data;

/// <summary>The portal model deliberately has no People, Users, Notes or general audit entities.</summary>
public sealed class SignatureDbContext(DbContextOptions<SignatureDbContext> options) : DbContext(options)
{
    public DbSet<FrozenSignatureDocument> FrozenSignatureDocuments => Set<FrozenSignatureDocument>();
    public DbSet<SignatureRequest> SignatureRequests => Set<SignatureRequest>();
    public DbSet<SignatureSession> SignatureSessions => Set<SignatureSession>();
    public DbSet<SignatureConsent> SignatureConsents => Set<SignatureConsent>();
    public DbSet<SignatureEvent> SignatureEvents => Set<SignatureEvent>();
    public DbSet<SignatureCompletion> SignatureCompletions => Set<SignatureCompletion>();
    public DbSet<SignaturePackage> SignaturePackages => Set<SignaturePackage>();
    public DbSet<SignatureOutbox> SignatureOutbox => Set<SignatureOutbox>();
    public DbSet<SignatureSourceDocument> SignatureSourceDocuments => Set<SignatureSourceDocument>();
    public DbSet<SignatureDatabaseEnvironment> SignatureDatabaseEnvironment => Set<SignatureDatabaseEnvironment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => SignaturePersistenceModel.Configure(modelBuilder);
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        SignaturePersistenceModel.ProtectWrites(ChangeTracker);
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        SignaturePersistenceModel.ProtectWrites(ChangeTracker);
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}
