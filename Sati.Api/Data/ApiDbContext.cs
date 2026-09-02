using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;

namespace Sati.Api.Data;

internal sealed class ApiDbContext(DbContextOptions<ApiDbContext> options) : DbContext(options)
{
    public DbSet<ServerUser> Users => Set<ServerUser>();
    public DbSet<ServerPerson> People => Set<ServerPerson>();
    public DbSet<ServerForm> Forms => Set<ServerForm>();
    public DbSet<ServerNote> Notes => Set<ServerNote>();
    public DbSet<ServerSettings> Settings => Set<ServerSettings>();
    public DbSet<ServerScratchpad> Scratchpads => Set<ServerScratchpad>();
    public DbSet<ServerScratchpadComment> ScratchpadComments => Set<ServerScratchpadComment>();
    public DbSet<ServerExemptDate> ExemptDates => Set<ServerExemptDate>();
    public DbSet<ServerIncentive> Incentives => Set<ServerIncentive>();
    public DbSet<ServerPersonContact> PersonContacts => Set<ServerPersonContact>();
    public DbSet<ServerPersonProvider> PersonProviders => Set<ServerPersonProvider>();
    public DbSet<ServerProviderContact> ProviderContacts => Set<ServerProviderContact>();
    public DbSet<ServerAgency> Agencies => Set<ServerAgency>();
    public DbSet<ServerBillingPeriod> BillingPeriods => Set<ServerBillingPeriod>();
    public DbSet<ServerClaimLine> ClaimLines => Set<ServerClaimLine>();
    public DbSet<ServerEdiGeneration> EdiGenerations => Set<ServerEdiGeneration>();
    public DbSet<ServerBillingSubmissionEvent> BillingSubmissionEvents => Set<ServerBillingSubmissionEvent>();
    public DbSet<ServerRemittanceClaimOutcome> RemittanceClaimOutcomes => Set<ServerRemittanceClaimOutcome>();
    public DbSet<ServerRemittanceDeposit> RemittanceDeposits => Set<ServerRemittanceDeposit>();
    public DbSet<ServerReviewItem> ReviewItems => Set<ServerReviewItem>();
    public DbSet<ServerAppointment> Appointments => Set<ServerAppointment>();
    public DbSet<ServerComprehensiveAssessment> ComprehensiveAssessments => Set<ServerComprehensiveAssessment>();
    public DbSet<ServerProvider> Providers => Set<ServerProvider>();
    public DbSet<ServerAtRequest> AtRequests => Set<ServerAtRequest>();
    public DbSet<ServerAtRequestItem> AtRequestItems => Set<ServerAtRequestItem>();
    public DbSet<ServerAuditEvent> AuditEvents => Set<ServerAuditEvent>();
    public DbSet<ServerPersonVersion> PersonVersions => Set<ServerPersonVersion>();
    public DbSet<ServerIncidentGroup> IncidentGroups => Set<ServerIncidentGroup>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ServerUser>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Username).HasMaxLength(50);
            entity.Property(x => x.Role).HasMaxLength(50);
            entity.Property(x => x.Permissions).HasConversion<int>();
        });

        modelBuilder.Entity<ServerPerson>(entity =>
        {
            entity.ToTable("People");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Revision).IsConcurrencyToken();
            // Must match the desktop model or the server writes a column it disagrees with.
            entity.Property(x => x.CredibleClientId).HasMaxLength(PersonSaveRules.CredibleClientIdMaxLength);
            entity.Property(x => x.FirstName)
                .IsRequired()
                .HasMaxLength(PersonSaveRules.FirstNameMaxLength);
            entity.Property(x => x.LastName)
                .IsRequired()
                .HasMaxLength(PersonSaveRules.LastNameMaxLength);
            // Lengths match the shadow declarations in SatiContext, which owns the
            // schema; a mismatch here surfaces as drift rather than as a silent
            // truncation of a key identifier.
            entity.Property(x => x.SsnKeyId).HasMaxLength(400);
            entity.Property(x => x.SsnLastFour).HasMaxLength(4);
            entity.Property(x => x.Email).HasMaxLength(254);
            entity.Property(x => x.VrCounselorName)
                .HasMaxLength(PersonSaveRules.VrStaffNameMaxLength);
            entity.Property(x => x.VrAssistantName)
                .HasMaxLength(PersonSaveRules.VrStaffNameMaxLength);
            entity.Property(x => x.IsTestData).HasDefaultValue(false);
            entity.Property(x => x.CaseManagerIsDhhsRepresentative);
            entity.Property(x => x.UsesModivcare);
            entity.Property(x => x.RepPayeeMonthlyIncome).HasColumnType("decimal(18,2)");
            entity.Property(x => x.RepPayeeRegularCheckRequestNeeds)
                .HasMaxLength(Sati.Contracts.V1.RepresentativePayeeRules.MaxRegularCheckRequestNeedsLength);
            entity.HasMany(x => x.Forms)
                .WithOne()
                .HasForeignKey(x => x.PersonId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ServerForm>(entity =>
        {
            entity.ToTable("Forms");
            entity.HasKey(x => x.Id);
            entity.Ignore(x => x.IsCompliant);
            // 40 to match SatiContext.FormTypeMaxLength — the two models describe the
            // same physical column and the migration chain narrowed it so it could be
            // indexed. Declaring 50 here would let the server accept a value the
            // column cannot hold.
            entity.Property(x => x.Type).HasMaxLength(40);
            // Mirrors IX_Forms_PersonId_Type_DueDate from the Sati.Persistence chain,
            // which owns the migration that creates it. Declared here so the server's
            // model matches the database it writes to: a person has exactly one form
            // of a given type for a given due date.
            entity.HasIndex(x => new { x.PersonId, x.Type, x.DueDate })
                  .IsUnique()
                  .HasDatabaseName("IX_Forms_PersonId_Type_DueDate");
        });

        modelBuilder.Entity<ServerNote>(entity =>
        {
            entity.ToTable("Notes");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Revision).IsConcurrencyToken();
        });

        modelBuilder.Entity<ServerSettings>(entity =>
        {
            entity.ToTable("Settings");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Revision).IsConcurrencyToken();
            entity.Property(x => x.BillingComplianceRequirements)
                .HasConversion<int>();
            entity.HasIndex(x => x.AgencyId).IsUnique();
            entity.Property(x => x.BaseIncentive).HasColumnType("decimal(18,2)");
            entity.Property(x => x.PerUnitIncentive).HasColumnType("decimal(18,2)");
            entity.Property(x => x.PassthroughRate).HasColumnType("decimal(5,4)");
            entity.Property(x => x.SalesTaxRate).HasColumnType("decimal(5,4)");
            entity.Property(x => x.VrAssistantTitle)
                .HasMaxLength(VocationalRehabilitationProfile.AssistantTitleMaxLength)
                .HasDefaultValue(VocationalRehabilitationProfile.DefaultAssistantTitle);
        });

        modelBuilder.Entity<ServerScratchpad>(entity =>
        {
            entity.ToTable("Scratchpad");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Revision).IsConcurrencyToken();
            entity.HasIndex(x => new { x.UserId, x.Date }).IsUnique();
        });

        modelBuilder.Entity<ServerScratchpadComment>(entity =>
        {
            entity.ToTable("ScratchpadComments");
            entity.HasKey(x => x.Id);
        });

        modelBuilder.Entity<ServerExemptDate>(entity =>
        {
            entity.ToTable("ExemptDates");
            entity.HasKey(x => x.Id);
        });

        modelBuilder.Entity<ServerIncentive>(entity =>
        {
            entity.ToTable("Incentives");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.BaseIncentive).HasColumnType("decimal(18,2)");
            entity.Property(x => x.PerUnitIncentive).HasColumnType("decimal(18,2)");
            entity.HasIndex(x => new { x.UserId, x.Month, x.Year }).IsUnique();
        });

        modelBuilder.Entity<ServerPersonContact>(entity =>
        {
            entity.ToTable("PersonContacts");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.FirstName).HasMaxLength(75);
            entity.Property(x => x.LastName).HasMaxLength(75);
            entity.Property(x => x.Kind).HasMaxLength(30);
            entity.Property(x => x.Relationship).HasMaxLength(100);
            entity.Property(x => x.Organization).HasMaxLength(150);
            entity.Property(x => x.Phone).HasMaxLength(30);
            entity.Property(x => x.Email).HasMaxLength(254);
            entity.HasIndex(x => new { x.PersonId, x.IsActive });
        });

        modelBuilder.Entity<ServerProviderContact>(entity =>
        {
            entity.ToTable("ProviderContacts");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(150);
            entity.Property(x => x.Role).HasMaxLength(100);
            entity.Property(x => x.Phone).HasMaxLength(30);
            entity.Property(x => x.Extension).HasMaxLength(10);
            entity.Property(x => x.Email).HasMaxLength(254);
            entity.HasIndex(x => new { x.ProviderId, x.SortOrder });
            // Mirrors the desktop context: one "try this person first" per entry.
            entity.HasIndex(x => x.ProviderId)
                  .IsUnique()
                  .HasFilter("[IsPrimary] = 1")
                  .HasDatabaseName("IX_ProviderContacts_OnePrimary");
        });

        modelBuilder.Entity<ServerPersonProvider>(entity =>
        {
            entity.ToTable("PersonProviders");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Role).HasMaxLength(ConsumerProviderRules.MaxRoleLength);
            entity.HasIndex(x => new { x.PersonId, x.EndDate });
            // Mirrors the desktop context: both rules are enforced by the database as well
            // as by the routes, and both filters key on EndDate IS NULL because an ended
            // relationship constrains nothing.
            entity.HasIndex(x => x.PersonId)
                  .IsUnique()
                  .HasFilter("[IsPrimaryCare] = 1 AND [EndDate] IS NULL")
                  .HasDatabaseName("IX_PersonProviders_OneCurrentPrimaryCare");
            entity.HasIndex(x => new { x.PersonId, x.ProviderId })
                  .IsUnique()
                  .HasFilter("[EndDate] IS NULL")
                  .HasDatabaseName("IX_PersonProviders_OneCurrentLinkPerProvider");
        });

        modelBuilder.Entity<ServerAgency>(entity =>
        {
            entity.ToTable("Agencies");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.BillingUnitRate).HasColumnType("decimal(18,2)");
        });

        modelBuilder.Entity<ServerBillingPeriod>(entity =>
        {
            entity.ToTable("BillingPeriods");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Status).IsConcurrencyToken();
            entity.HasIndex(x => new { x.UserId, x.Month, x.Year }).IsUnique();
        });

        modelBuilder.Entity<ServerClaimLine>(entity =>
        {
            entity.ToTable("ClaimLines");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.NoteId).IsUnique();
            entity.Property(x => x.Units).IsRequired().HasColumnType("decimal(18,2)");
            entity.Property(x => x.ChargeAmount).HasColumnType("decimal(18,2)");
            entity.HasOne<ServerBillingPeriod>()
                .WithMany(x => x.Lines)
                .HasForeignKey(x => x.BillingPeriodId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ServerEdiGeneration>(entity =>
        {
            entity.ToTable("EdiGenerations");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.AgencyId, x.ActorUserId, x.IdempotencyKey }).IsUnique();
            entity.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(32);
            entity.Property(x => x.FileName).IsRequired().HasMaxLength(260);
            entity.Property(x => x.Content).IsRequired();
            entity.HasOne<ServerBillingPeriod>()
                .WithMany()
                .HasForeignKey(x => x.BillingPeriodId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ServerBillingSubmissionEvent>(entity =>
        {
            entity.ToTable("BillingSubmissionEvents");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.AgencyId, item.OccurredAtUtc });
            entity.Property(item => item.Reference).HasMaxLength(80);
            entity.Property(item => item.ResponseType).HasMaxLength(20);
            entity.Property(item => item.ResponseCode).HasMaxLength(30);
            entity.Property(item => item.Explanation).HasMaxLength(500);
            entity.HasOne<ServerBillingPeriod>()
                .WithMany()
                .HasForeignKey(item => item.BillingPeriodId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ServerRemittanceClaimOutcome>(entity =>
        {
            entity.ToTable("RemittanceClaimOutcomes");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.AgencyId, item.ReceivedAtUtc });
            entity.Property(item => item.ClaimReference).HasMaxLength(80);
            entity.Property(item => item.PayerName).HasMaxLength(100);
            entity.Property(item => item.ReasonCode).HasMaxLength(30);
            entity.Property(item => item.Explanation).HasMaxLength(500);
            entity.Property(item => item.PaymentReference).HasMaxLength(80);
            entity.Property(item => item.BilledAmount).HasColumnType("decimal(18,2)");
            entity.Property(item => item.AllowedAmount).HasColumnType("decimal(18,2)");
            entity.Property(item => item.PaidAmount).HasColumnType("decimal(18,2)");
            entity.Property(item => item.AdjustmentAmount).HasColumnType("decimal(18,2)");
            entity.Property(item => item.PatientResponsibilityAmount).HasColumnType("decimal(18,2)");
            entity.HasOne<ServerBillingPeriod>()
                .WithMany()
                .HasForeignKey(item => item.BillingPeriodId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ServerRemittanceDeposit>(entity =>
        {
            entity.ToTable("RemittanceDeposits");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.AgencyId, item.ReceivedAtUtc });
            entity.Property(item => item.PaymentReference).IsRequired().HasMaxLength(80);
            entity.Property(item => item.PayerName).IsRequired().HasMaxLength(100);
            entity.Property(item => item.ProviderLevelAdjustmentSummary).HasMaxLength(500);
            entity.Property(item => item.ClaimPaymentAmount).HasColumnType("decimal(18,2)");
            entity.Property(item => item.ProviderLevelAdjustmentAmount).HasColumnType("decimal(18,2)");
            entity.Property(item => item.RemittancePaymentAmount).HasColumnType("decimal(18,2)");
            entity.Property(item => item.EftDepositAmount).HasColumnType("decimal(18,2)");
        });

        modelBuilder.Entity<ServerReviewItem>(entity =>
        {
            entity.ToTable("ReviewItems");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Category).HasMaxLength(30);
            entity.HasIndex(x => new { x.PersonId, x.CycleAnchor, x.Quarter, x.Category, x.SlotIndex }).IsUnique();
        });

        modelBuilder.Entity<ServerAppointment>(entity =>
        {
            entity.ToTable("Appointments");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ProviderName).HasMaxLength(100);
            entity.HasOne<ServerReviewItem>()
                .WithOne(x => x.Appointment)
                .HasForeignKey<ServerAppointment>(x => x.ReviewItemId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ServerComprehensiveAssessment>(entity =>
        {
            entity.ToTable("ComprehensiveAssessments");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Status).HasMaxLength(30);
            entity.Property(x => x.DocumentJson).IsRequired();
            entity.Property(x => x.Revision).IsConcurrencyToken();
            entity.HasIndex(x => new { x.PersonId, x.Version }).IsUnique();
        });

        modelBuilder.Entity<ServerProvider>(entity =>
        {
            entity.ToTable("Providers"); entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.AgencyId, x.Name });
            entity.Property(x => x.Type).HasMaxLength(20);
            // Restrict rather than SetNull: silently promoting a subtree to top level splits
            // the hierarchy with nothing in the interface showing it. The route refuses the
            // delete and names the affiliated entries.
            entity.Property(x => x.MedicalKind).HasMaxLength(20);
            entity.HasOne<ServerProvider>()
                  .WithMany()
                  .HasForeignKey(x => x.ParentProviderId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => x.ParentProviderId);
            // Mirrors the desktop context: durable organization identifiers, unique
            // within an agency when present. See DECISIONS.md for why these exist
            // before the Organization registry they will eventually resolve against.
            entity.Property(x => x.Npi).HasMaxLength(10);
            entity.Property(x => x.MaineCareProviderId).HasMaxLength(30);
            entity.HasIndex(x => new { x.AgencyId, x.Npi })
                  .IsUnique()
                  .HasFilter("[Npi] IS NOT NULL");
            entity.HasIndex(x => new { x.AgencyId, x.MaineCareProviderId })
                  .IsUnique()
                  .HasFilter("[MaineCareProviderId] IS NOT NULL");
        });
        modelBuilder.Entity<ServerAtRequest>(entity =>
        {
            entity.ToTable("ATRequests"); entity.HasKey(x => x.Id);
            entity.Property(x => x.Revision).IsConcurrencyToken();
            entity.Property(x => x.Status).HasMaxLength(20);
            entity.Property(x => x.SalesTax).HasColumnType("decimal(18,2)");
            entity.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.ATRequestId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ServerAtRequestItem>(entity =>
        {
            entity.ToTable("ATRequestItems"); entity.HasKey(x => x.Id);
            entity.Property(x => x.ItemCost).HasColumnType("decimal(18,2)");
        });
        modelBuilder.Entity<ServerAuditEvent>(entity =>
        {
            entity.ToTable("AuditEvents");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.EventId).IsUnique();
            entity.HasIndex(x => new { x.AgencyId, x.OccurredAtUtc });
            entity.Property(x => x.Action).IsRequired().HasMaxLength(100);
            entity.Property(x => x.ResourceType).IsRequired().HasMaxLength(100);
            entity.Property(x => x.ResourceId).HasMaxLength(100);
            entity.Property(x => x.CorrelationId).IsRequired().HasMaxLength(100);
            entity.Property(x => x.MetadataJson).IsRequired().HasMaxLength(4_000);
        });
        modelBuilder.Entity<ServerIncidentGroup>(entity =>
        {
            entity.ToTable("IncidentGroups");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.AgencyId, x.Scope, x.Source, x.Operation, x.ExceptionFingerprint }).IsUnique();
            entity.HasIndex(x => new { x.AgencyId, x.LastSeenUtc });
            entity.Property(x => x.Source).IsRequired().HasMaxLength(20);
            entity.Property(x => x.Scope).IsRequired().HasMaxLength(20);
            entity.Property(x => x.Severity).IsRequired().HasMaxLength(20);
            entity.Property(x => x.Operation).IsRequired().HasMaxLength(80);
            entity.Property(x => x.FirstRelease).IsRequired().HasMaxLength(30);
            entity.Property(x => x.LastRelease).IsRequired().HasMaxLength(30);
            entity.Property(x => x.ExceptionFingerprint).IsRequired().HasMaxLength(64);
            entity.Property(x => x.Status).IsRequired().HasMaxLength(20);
            entity.Property(x => x.LastReference).IsRequired().HasMaxLength(40);
            entity.Property(x => x.LastActorRole).IsRequired().HasMaxLength(30);
            entity.HasOne<ServerAgency>()
                .WithMany()
                .HasForeignKey(x => x.AgencyId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<ServerPersonVersion>(entity =>
        {
            entity.ToTable("PersonVersions");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.PersonId, x.Version }).IsUnique();
            entity.HasIndex(x => new { x.AgencyId, x.ChangedAtUtc });
            entity.Property(x => x.ActorDisplayName).IsRequired().HasMaxLength(150);
            entity.Property(x => x.ChangeKind).IsRequired().HasMaxLength(30);
            entity.Property(x => x.CorrelationId).IsRequired().HasMaxLength(100);
            entity.Property(x => x.SnapshotGzip).IsRequired();
            entity.Property(x => x.ChangesGzip).IsRequired();
            entity.HasOne(x => x.Person)
                .WithMany()
                .HasForeignKey(x => x.PersonId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnsureAuditEventsAreAppendOnly();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        EnsureAuditEventsAreAppendOnly();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void EnsureAuditEventsAreAppendOnly()
    {
        if (ChangeTracker.Entries<ServerAuditEvent>()
                .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted) ||
            ChangeTracker.Entries<ServerPersonVersion>()
                .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted) ||
            ChangeTracker.Entries<ServerBillingSubmissionEvent>()
                .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted) ||
            ChangeTracker.Entries<ServerRemittanceClaimOutcome>()
                .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted) ||
            ChangeTracker.Entries<ServerRemittanceDeposit>()
                .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Audit, Person history, and billing exchange records are append-only.");
        }
    }
}

internal sealed class ServerUser
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Salt { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public UserPermissions Permissions { get; set; }
    public int? SupervisorId { get; set; }
    public int AgencyId { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
}

internal sealed class ServerPerson
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int Revision { get; set; } = 1;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public DateTime BirthDate { get; set; }
    public int Gender { get; set; }
    public DateTime? EffectiveDate { get; set; }
    public string? Bio { get; set; }
    public string? Journal { get; set; }
    public int Waiver { get; set; }
    public int? AgencyId { get; set; }
    public bool IsTestData { get; set; }
    public string? MaineCareId { get; set; }
    public string? DiagnosisCode { get; set; }
    public int? PlaceOfService { get; set; }
    public string? EvergreenId { get; set; }
    public string? CredibleClientId { get; set; }
    public bool OpenWithVR { get; set; }
    public string? VrCounselorName { get; set; }
    public string? VrAssistantName { get; set; }
    public bool HasGuardian { get; set; }
    public string? GuardianName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? BillingStreet { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingZip { get; set; }
    public string? PrimaryCareProvider { get; set; }
    public string? HealthcareSystemName { get; set; }
    public bool CaseManagerIsRepPayee { get; set; }
    public bool CaseManagerIsDhhsRepresentative { get; set; }
    public bool UsesModivcare { get; set; }
    public decimal? RepPayeeMonthlyIncome { get; set; }
    public string? RepPayeeRegularCheckRequestNeeds { get; set; }
    public bool HasHomeSupport { get; set; }
    public bool HasSelfDirectedHomeSupport { get; set; }
    public bool HasSharedLiving { get; set; }
    public bool HasCommunitySupport1To1 { get; set; }
    public bool HasCommunitySupportSelfDirected { get; set; }
    public bool HasCommunitySupportDayProgram { get; set; }
    public int DayProgramCount { get; set; }
    public bool HasEmploymentSpecialist { get; set; }
    public bool HasWorkSupports { get; set; }
    public bool IsEmployed { get; set; }

    // Encrypted SSN. Real properties here, and deliberately shadow properties on the
    // desktop's SatiContext, because this is the only process allowed to hold the
    // plaintext. Every part is required to decrypt and none is secret alone — the row
    // is inert without the Key Vault key named by SsnKeyId. See EnvelopeProtector.
    public byte[]? SsnCiphertext { get; set; }
    public byte[]? SsnNonce { get; set; }
    public byte[]? SsnTag { get; set; }
    public byte[]? SsnWrappedKey { get; set; }
    public string? SsnKeyId { get; set; }

    /// <summary>
    /// The last four digits, stored in the clear so a masked list costs no Key Vault
    /// unwraps. Safe to project into a DTO; the other six columns never are.
    /// </summary>
    public string? SsnLastFour { get; set; }

    public List<ServerForm> Forms { get; set; } = [];
}

internal sealed class ServerForm
{
    public int Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
    public int PersonId { get; set; }
    public DateTime? CompletedDate { get; set; }
    public DateTime? OpenedDate { get; set; }

    // Derived, matching Sati.Models.Form. The stored column was dropped in
    // AddDerivedFormCompliance: a second field for the same fact is a rule with no
    // owner, and the two disagreed on 147 rows.
    public bool IsCompliant => CompletedDate.HasValue;

    public void ApplyCompletion(DateTime? completedOn) => CompletedDate = completedOn?.Date;
}

internal sealed class ServerNote
{
    public int Id { get; set; }
    public string Narrative { get; set; } = string.Empty;
    public DateTime? EventDate { get; set; }
    public int? Status { get; set; }
    public int? Minutes { get; set; }
    public int? StartTime { get; set; }
    public int Revision { get; set; } = 1;
    public int PersonId { get; set; }
    public int? FormType { get; set; }
    public int? NoteType { get; set; }
    public int? AgencyId { get; set; }
    public string? ReturnReason { get; set; }
    public int? ReturnedById { get; set; }
    public int? ApprovedById { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? ReturnedAt { get; set; }
    public string? CaseManagerJustification { get; set; }
    public string? VisitDocumentationJson { get; set; }
    public bool ComplianceOverride { get; set; }
    public string? OverrideReason { get; set; }
    public int? OverrideApprovedById { get; set; }
    public DateTime? OverrideApprovedAt { get; set; }
}

internal sealed class ServerSettings
{
    public int Id { get; set; }
    public int AgencyId { get; set; }
    public int Revision { get; set; } = 1;
    public bool AllowCredibleProfileUpdates { get; set; }
    public string VrAssistantTitle { get; set; } =
        VocationalRehabilitationProfile.DefaultAssistantTitle;
    public BillingComplianceRequirements BillingComplianceRequirements { get; set; } =
        BillingComplianceGate.DefaultRequirements;
    public int AbandonedAfterDays { get; set; } = 7;
    public int ProductivityThreshold { get; set; } = 100;
    public decimal BaseIncentive { get; set; }
    public decimal PerUnitIncentive { get; set; }
    public decimal PassthroughRate { get; set; } = 0.15m;
    public decimal SalesTaxRate { get; set; } = 0.055m;
    public int? DefaultPassthroughProviderId { get; set; }
    public string VisitTemplate { get; set; } = string.Empty;
    public string ContactTemplate { get; set; } = string.Empty;
    public string DocumentationTemplate { get; set; } = string.Empty;
    public string HealthcareSystemsJson { get; set; } = "[\"Other\"]";
    public bool ExcludeMonday { get; set; }
    public bool ExcludeTuesday { get; set; }
    public bool ExcludeWednesday { get; set; }
    public bool ExcludeThursday { get; set; }
    public bool ExcludeFriday { get; set; }
    public bool ExcludeNewYearsDay { get; set; } = true;
    public bool ExcludeMLKDay { get; set; }
    public bool ExcludePresidentsDay { get; set; }
    public bool ExcludeMemorialDay { get; set; } = true;
    public bool ExcludeJuneteenth { get; set; }
    public bool ExcludeIndependenceDay { get; set; } = true;
    public bool ExcludeLaborDay { get; set; } = true;
    public bool ExcludeIndigenousPeoplesDay { get; set; }
    public bool ExcludeVeteransDay { get; set; }
    public bool ExcludeThanksgiving { get; set; } = true;
    public bool ExcludeDayAfterThanksgiving { get; set; } = true;
    public bool ExcludeChristmas { get; set; } = true;
    public int ReviewOpenDaysBefore { get; set; } = 10;
    public int ReviewDaysAfterDue { get; set; } = 10;
    public int PcpOpenDaysBefore { get; set; } = 90;
    public int PcpDaysAfterDue { get; set; } = 30;
    public int CompAssessmentOpenDaysBefore { get; set; } = 30;
    public int CompAssessmentDaysAfterDue { get; set; } = 30;
    public int ReclassificationOpenDaysBefore { get; set; } = 15;
    public int ReclassificationDaysAfterDue { get; set; }
    public int SafetyPlanOpenDaysBefore { get; set; } = 60;
    public int SafetyPlanDaysAfterDue { get; set; } = 30;
    public int PrivacyPracticesOpenDaysBefore { get; set; } = 30;
    public int PrivacyPracticesDaysAfterDue { get; set; } = 30;
    public int ReleaseAgencyOpenDaysBefore { get; set; } = 30;
    public int ReleaseAgencyDaysAfterDue { get; set; } = 30;
    public int ReleaseDhhsOpenDaysBefore { get; set; } = 30;
    public int ReleaseDhhsDaysAfterDue { get; set; } = 30;
    public int ReleaseMedicalOpenDaysBefore { get; set; } = 30;
    public int ReleaseMedicalDaysAfterDue { get; set; } = 30;
    public int Q4RDaysBeforeAnniversary { get; set; } = 5;
    public int PcpDaysBeforeAnniversary { get; set; }
    public int CompAssessmentDaysBeforeAnniversary { get; set; } = 60;
    public int ReclassificationDaysBeforeAnniversary { get; set; } = 30;
    public int SafetyPlanDaysBeforeAnniversary { get; set; }
    public int PrivacyPracticesDaysBeforeAnniversary { get; set; }
    public int ReleaseAgencyDaysBeforeAnniversary { get; set; }
    public int ReleaseDhhsDaysBeforeAnniversary { get; set; }
    public int ReleaseMedicalDaysBeforeAnniversary { get; set; }
}

internal sealed class ServerScratchpad
{
    public int Revision { get; set; } = 1;
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateTime Date { get; set; }
    public string Content { get; set; } = string.Empty;
}

internal sealed class ServerScratchpadComment
{
    public int Id { get; set; }
    public int ScratchpadId { get; set; }
    public int AuthorUserId { get; set; }
    public string AuthorDisplayName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public string Content { get; set; } = string.Empty;
}

internal sealed class ServerExemptDate
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateTime Date { get; set; }
    public string? Reason { get; set; }
}

internal sealed class ServerIncentive
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int Month { get; set; }
    public int Year { get; set; }
    public int DaysScheduled { get; set; }
    public decimal BaseIncentive { get; set; }
    public decimal PerUnitIncentive { get; set; }
    public int UnitsPerDay { get; set; }
    public string ExcludedDatesJson { get; set; } = "[]";
}

internal sealed class ServerPersonContact
{
    public int Id { get; set; }
    public int PersonId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Kind { get; set; } = "Personal";
    public string? Relationship { get; set; }
    public string? Organization { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public bool IsEmergencyContact { get; set; }
    public bool HasActiveRelease { get; set; }
    public bool IsActive { get; set; } = true;
}

// A named person at a provider. Distinct from ServerProvider.PrimaryContact/Phone, which are the
// organization's general directory contact.
internal sealed class ServerProviderContact
{
    public int Id { get; set; }
    public int ProviderId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Role { get; set; }
    public string? Phone { get; set; }
    public string? Extension { get; set; }
    public string? Email { get; set; }
    public bool IsPrimary { get; set; }
    public int SortOrder { get; set; }
}

internal sealed class ServerPersonProvider
{
    public int Id { get; set; }
    public int PersonId { get; set; }
    public int ProviderId { get; set; }
    public string? Role { get; set; }
    public bool IsPrimaryCare { get; set; }
    public DateTime? StartDate { get; set; }
    // EndDate alone says whether the link is current; there is deliberately no active flag.
    public DateTime? EndDate { get; set; }
    public bool HasActiveRelease { get; set; }
    public int SortOrder { get; set; }
}

internal sealed class ServerAgency
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Npi { get; set; }
    public string? TaxId { get; set; }
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Zip { get; set; }
    public string? BillingProcedureCode { get; set; }
    public string? BillingModifier { get; set; }
    public decimal? BillingUnitRate { get; set; }
    public string? EdiSubmitterId { get; set; }
    public string? EdiPayerName { get; set; }
    public string? EdiPayerId { get; set; }
    public string? EdiContactName { get; set; }
    public string? EdiContactPhone { get; set; }
}

internal sealed class ServerBillingPeriod
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int Month { get; set; }
    public int Year { get; set; }
    public int Status { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public List<ServerClaimLine> Lines { get; set; } = [];
}

internal sealed class ServerClaimLine
{
    public int Id { get; set; }
    public int NoteId { get; set; }
    public int BillingPeriodId { get; set; }
    public DateTime DateOfService { get; set; }
    public string ProcedureCode { get; set; } = string.Empty;
    public string? ProcedureModifier { get; set; }
    public decimal Units { get; set; }
    public decimal ChargeAmount { get; set; }
    public string ClientMaineCareId { get; set; } = string.Empty;
    public string RenderingProviderNpi { get; set; } = string.Empty;
    public string DiagnosisCode { get; set; } = string.Empty;
    public int PlaceOfService { get; set; }
    public string? ClaimSnapshotJson { get; set; }
    public bool IsComplianceException { get; set; }
    public string? ComplianceExceptionReason { get; set; }
}

internal sealed class ServerEdiGeneration
{
    public long Id { get; set; }
    public int AgencyId { get; set; }
    public int ActorUserId { get; set; }
    public int BillingPeriodId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public bool IsTest { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

internal sealed class ServerBillingSubmissionEvent
{
    public long Id { get; set; }
    public int AgencyId { get; set; }
    public int BillingPeriodId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public BillingSubmissionStage Stage { get; set; }
    public string? Reference { get; set; }
    public string? ResponseType { get; set; }
    public string? ResponseCode { get; set; }
    public string? Explanation { get; set; }
    public bool IsSynthetic { get; set; }
}

internal sealed class ServerRemittanceClaimOutcome
{
    public long Id { get; set; }
    public int AgencyId { get; set; }
    public int? BillingPeriodId { get; set; }
    public string ClaimReference { get; set; } = string.Empty;
    public string PayerName { get; set; } = string.Empty;
    public DateTime ReceivedAtUtc { get; set; }
    public DateTime? PaymentDate { get; set; }
    public RemittanceClaimStatus Status { get; set; }
    public decimal BilledAmount { get; set; }
    public decimal? AllowedAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal AdjustmentAmount { get; set; }
    public decimal PatientResponsibilityAmount { get; set; }
    public string? ReasonCode { get; set; }
    public string? Explanation { get; set; }
    public string? PaymentReference { get; set; }
    public bool IsSynthetic { get; set; }
}

internal sealed class ServerRemittanceDeposit
{
    public long Id { get; set; }
    public int AgencyId { get; set; }
    public string PaymentReference { get; set; } = string.Empty;
    public string PayerName { get; set; } = string.Empty;
    public DateTime ReceivedAtUtc { get; set; }
    public DateTime? PaymentDate { get; set; }
    public decimal ClaimPaymentAmount { get; set; }
    public decimal ProviderLevelAdjustmentAmount { get; set; }
    public string? ProviderLevelAdjustmentSummary { get; set; }
    public decimal RemittancePaymentAmount { get; set; }
    public decimal? EftDepositAmount { get; set; }
    public bool IsSynthetic { get; set; }
}

internal sealed class ServerReviewItem
{
    public int Id { get; set; }
    public int PersonId { get; set; }
    public DateTime CycleAnchor { get; set; }
    public int Quarter { get; set; }
    public string Category { get; set; } = string.Empty;
    public DateTime? RequestedDate { get; set; }
    public DateTime? ReceivedDate { get; set; }
    public DateTime? LoggedDate { get; set; }
    public int SlotIndex { get; set; }
    public ServerAppointment? Appointment { get; set; }
}

internal sealed class ServerAppointment
{
    public int Id { get; set; }
    public int ReviewItemId { get; set; }
    public DateTime Date { get; set; }
    public string? ProviderName { get; set; }
}

internal sealed class ServerComprehensiveAssessment
{
    public int Id { get; set; }
    public int PersonId { get; set; }
    public int AuthorUserId { get; set; }
    public string Status { get; set; } = "Draft";
    public int Version { get; set; } = 1;
    public int Revision { get; set; } = 1;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public int? ApprovedByUserId { get; set; }
    public string DocumentJson { get; set; } = "{}";
}

internal sealed class ServerProvider
{
    public int Id { get; set; }
    public int AgencyId { get; set; }
    public string Type { get; set; } = "Waiver";
    public string Name { get; set; } = string.Empty;
    // Affiliation, mirroring the desktop entity. MedicalKind is stored as a string like
    // Type; ParentProviderId is a self-reference with no navigation, because the rule that
    // reads it works on the agency's rows in memory through ProviderAffiliation.
    public string? MedicalKind { get; set; }
    public int? ParentProviderId { get; set; }
    public string? Npi { get; set; }
    public string? MaineCareProviderId { get; set; }
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Zip { get; set; }
    public string? PrimaryContact { get; set; }
    public string? Phone { get; set; }
    public int OfferedServices { get; set; }
    public bool ProvidesPassthroughService { get; set; }
    public string? BillingLocationEis { get; set; }
    public string? ProgramContact { get; set; }
    public string? BillingContact { get; set; }
}

internal sealed class ServerAtRequest
{
    public int Id { get; set; }
    public int Revision { get; set; } = 1;
    public int PersonId { get; set; }
    public string? ClientName { get; set; }
    public string? ClientEvergreenId { get; set; }
    public string? CaseManagerName { get; set; }
    public string? CaseManagerEmail { get; set; }
    public string? CaseManagerPhone { get; set; }
    public string? CaseManagerAgency { get; set; }
    public string? VendorName { get; set; }
    public string? VendorBillingLocation { get; set; }
    public string? VendorProgramContact { get; set; }
    public string? VendorBillingContact { get; set; }
    public decimal SalesTax { get; set; }
    public bool SalesTaxOverridden { get; set; }
    public DateTime? SubmittedDate { get; set; }
    public DateTime? DecisionDate { get; set; }
    public string Status { get; set; } = "Development";

    // Attestation. Written only by the publish and reopen routes, from the
    // authenticated actor — never from a save payload. See AtRequestPublication.
    // The passthrough rate this request was published under, read from agency
    // settings at publication. Null on a draft, where the live rate applies.
    public decimal? PassthroughRate { get; set; }

    public string? SignedByName { get; set; }
    public string? SignedByRole { get; set; }
    public int? SignedByUserId { get; set; }
    public DateTime? SignedAtUtc { get; set; }
    public string? AttestationStatement { get; set; }

    public byte[]? SnapshotPng { get; set; }
    public List<ServerAtRequestItem> Items { get; set; } = [];
}

internal sealed class ServerAtRequestItem
{
    public int Id { get; set; }
    public int ATRequestId { get; set; }
    public string? Name { get; set; }
    public decimal ItemCost { get; set; }
    public int Quantity { get; set; }
    public string? Url { get; set; }

    // Pasted evidence clip. Heavy column; the AT request list projection never
    // touches item rows at all, so it stays out of queue reads by construction.
    public byte[]? ScreenshotPng { get; set; }
}

internal sealed class ServerAuditEvent
{
    public long Id { get; set; }
    public Guid EventId { get; set; } = Guid.NewGuid();
    public int AgencyId { get; set; }
    public int ActorUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public string? ResourceId { get; set; }
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
    public string CorrelationId { get; set; } = string.Empty;
    public string MetadataJson { get; set; } = "{}";
}

internal sealed class ServerPersonVersion
{
    public long Id { get; set; }
    public int PersonId { get; set; }
    public ServerPerson Person { get; set; } = null!;
    public int AgencyId { get; set; }
    public int ActorUserId { get; set; }
    public string ActorDisplayName { get; set; } = string.Empty;
    public int Version { get; set; }
    public string ChangeKind { get; set; } = string.Empty;
    public DateTime ChangedAtUtc { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public byte[] SnapshotGzip { get; set; } = [];
    public byte[] ChangesGzip { get; set; } = [];
}

internal sealed class ServerIncidentGroup
{
    public long Id { get; set; }
    public int AgencyId { get; set; }
    public string Scope { get; set; } = "Agency";
    public string Source { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string FirstRelease { get; set; } = string.Empty;
    public string LastRelease { get; set; } = string.Empty;
    public string ExceptionFingerprint { get; set; } = string.Empty;
    public string Status { get; set; } = "Open";
    public int OccurrenceCount { get; set; } = 1;
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public string LastReference { get; set; } = string.Empty;
    public string LastActorRole { get; set; } = string.Empty;
}
