using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Models;
using Sati.Models.Billing;
using Sati.Models.Assessments;

namespace Sati.Data
{
    public class SatiContext : DbContext
    {
        public DbSet<Agency> Agencies { get; set; }
        public DbSet<Person> People { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<Form> Forms { get; set; }
        public DbSet<Note> Notes { get; set; }
        public DbSet<Settings> Settings { get; set; }
        public DbSet<Scratchpad> Scratchpad { get; set; }
        public DbSet<ScratchpadComment> ScratchpadComments { get; set; }
        public DbSet<Incentive> Incentives { get; set; }
        public DbSet<BillingPeriod> BillingPeriods { get; set; }
        public DbSet<ClaimLine> ClaimLines { get; set; }
        public DbSet<EdiGeneration> EdiGenerations { get; set; }
        public DbSet<ExemptDate> ExemptDates { get; set; }
        public DbSet<ReviewItem> ReviewItems { get; set; }
        public DbSet<Appointment> Appointments { get; set; }
        public DbSet<ATRequest> ATRequests { get; set; }
        public DbSet<ATRequestItem> ATRequestItems { get; set; }
        public DbSet<Provider> Providers { get; set; }
        public DbSet<ProviderContact> ProviderContacts { get; set; }
        public DbSet<PersonContact> PersonContacts { get; set; }
        public DbSet<PersonProvider> PersonProviders { get; set; }
        public DbSet<ComprehensiveAssessment> ComprehensiveAssessments { get; set; }
        public DbSet<AuditEvent> AuditEvents { get; set; }
        public DbSet<PersonVersion> PersonVersions { get; set; }
        public DbSet<IncidentGroup> IncidentGroups { get; set; }


        public SatiContext(DbContextOptions<SatiContext> options) : base(options)
        {
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
            if (ChangeTracker.Entries<AuditEvent>()
                    .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted) ||
                ChangeTracker.Entries<PersonVersion>()
                    .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
            {
                throw new InvalidOperationException("Audit and Person history records are append-only.");
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Agency>(entity =>
            {
                entity.HasKey(a => a.Id);
                entity.Property(a => a.Name)
                      .IsRequired()
                      .HasMaxLength(100);
                entity.Property(a => a.BillingUnitRate).HasColumnType("decimal(18,2)");
                entity.HasData(
                                    new Agency { Id = 1, Name = "Internal" },
                                    new Agency { Id = 2, Name = "Sandbox Mode" });
            });

            modelBuilder.Entity<Provider>(entity =>
            {
                entity.HasKey(p => p.Id);
                entity.HasIndex(p => new { p.AgencyId, p.Name });
                entity.HasOne<Agency>()
                      .WithMany()
                      .HasForeignKey(p => p.AgencyId)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.Property(p => p.Name)
                      .IsRequired()
                      .HasMaxLength(150);

                // Enum-as-string, matching User.Role / ATRequest.Status. OfferedServices
                // is deliberately NOT converted — a [Flags] bitmask stores as its int,
                // the queryable, idiomatic form for flags.
                entity.Property(p => p.Type)
                      .HasConversion<string>()
                      .HasMaxLength(20);

                // Affiliation. MedicalKind stores as a string like Type; the parent is a
                // self-reference with no navigation property, because every read that needs
                // the chain already has the agency's rows in memory and walks them through
                // ProviderAffiliation.
                //
                // Restrict, not SetNull: clearing the link would silently promote a whole
                // subtree to top level, and a split hierarchy is invisible in the UI. The
                // services refuse the delete with a message naming the affiliated entries
                // instead of surfacing a foreign-key violation.
                entity.Property(p => p.MedicalKind)
                      .HasConversion<string>()
                      .HasMaxLength(20);
                entity.HasOne<Provider>()
                      .WithMany()
                      .HasForeignKey(p => p.ParentProviderId)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(p => p.ParentProviderId);

                entity.Property(p => p.Street).HasMaxLength(250);
                entity.Property(p => p.City).HasMaxLength(100);
                entity.Property(p => p.State).HasMaxLength(2);
                entity.Property(p => p.Zip).HasMaxLength(10);
                entity.Property(p => p.PrimaryContact).HasMaxLength(150);
                entity.Property(p => p.Phone).HasMaxLength(20);
                entity.Property(p => p.BillingLocationEis).HasMaxLength(150);
                entity.Property(p => p.ProgramContact).HasMaxLength(150);
                entity.Property(p => p.BillingContact).HasMaxLength(150);

                // Durable identifiers. Filtered unique indexes: within one agency the
                // same organization may not be entered twice under the same
                // identifier. NULL is excluded from the filter because most entries
                // legitimately have neither, and SQL Server would otherwise treat all
                // the NULLs as colliding.
                entity.Property(p => p.Npi).HasMaxLength(10);
                entity.Property(p => p.MaineCareProviderId).HasMaxLength(30);
                entity.HasIndex(p => new { p.AgencyId, p.Npi })
                      .IsUnique()
                      .HasFilter("[Npi] IS NOT NULL");
                entity.HasIndex(p => new { p.AgencyId, p.MaineCareProviderId })
                      .IsUnique()
                      .HasFilter("[MaineCareProviderId] IS NOT NULL");

                // The historical statewide default was seeded before providers
                // became tenant-owned. The tenant-scope migration assigns that row
                // to the active agency; tenant provisioning owns future defaults.
            });

            modelBuilder.Entity<ProviderContact>(entity =>
            {
                entity.HasKey(contact => contact.Id);
                entity.Property(contact => contact.Name).IsRequired().HasMaxLength(150);
                entity.Property(contact => contact.Role).HasMaxLength(100);
                entity.Property(contact => contact.Phone).HasMaxLength(30);
                entity.Property(contact => contact.Extension).HasMaxLength(10);
                entity.Property(contact => contact.Email).HasMaxLength(254);
                entity.HasIndex(contact => new { contact.ProviderId, contact.SortOrder });

                // Cascade: contacts are part of the directory entry, not independent records,
                // so they go with it. Deleting the entry itself is Admin-only and separately
                // refused while anything else still points at it.
                entity.HasOne<Provider>()
                      .WithMany()
                      .HasForeignKey(contact => contact.ProviderId)
                      .OnDelete(DeleteBehavior.Cascade);

                // One "try this person first" per entry, enforced rather than left to the form.
                entity.HasIndex(contact => contact.ProviderId)
                      .IsUnique()
                      .HasFilter("[IsPrimary] = 1")
                      .HasDatabaseName("IX_ProviderContacts_OnePrimary");
            });

            modelBuilder.Entity<ComprehensiveAssessment>(entity =>
            {
                entity.HasKey(a => a.Id);
                entity.Property(a => a.Status).HasConversion<string>().HasMaxLength(30);
                entity.Property(a => a.DocumentJson).IsRequired();
                entity.Property(a => a.Revision).IsConcurrencyToken();
                entity.HasIndex(a => new { a.PersonId, a.Version }).IsUnique();
                entity.HasOne(a => a.Person).WithMany().HasForeignKey(a => a.PersonId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(a => a.AuthorUser).WithMany().HasForeignKey(a => a.AuthorUserId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne<User>().WithMany().HasForeignKey(a => a.ApprovedByUserId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<AuditEvent>(entity =>
            {
                entity.HasKey(a => a.Id);
                entity.HasIndex(a => a.EventId).IsUnique();
                entity.HasIndex(a => new { a.AgencyId, a.OccurredAtUtc });
                entity.Property(a => a.Action).IsRequired().HasMaxLength(100);
                entity.Property(a => a.ResourceType).IsRequired().HasMaxLength(100);
                entity.Property(a => a.ResourceId).HasMaxLength(100);
                entity.Property(a => a.CorrelationId).IsRequired().HasMaxLength(100);
                entity.Property(a => a.MetadataJson).IsRequired().HasMaxLength(4_000);
            });

            modelBuilder.Entity<PersonVersion>(entity =>
            {
                entity.HasKey(version => version.Id);
                entity.HasIndex(version => new { version.PersonId, version.Version }).IsUnique();
                entity.HasIndex(version => new { version.AgencyId, version.ChangedAtUtc });
                entity.Property(version => version.ActorDisplayName).IsRequired().HasMaxLength(150);
                entity.Property(version => version.ChangeKind).IsRequired().HasMaxLength(30);
                entity.Property(version => version.CorrelationId).IsRequired().HasMaxLength(100);
                entity.Property(version => version.SnapshotGzip).IsRequired();
                entity.Property(version => version.ChangesGzip).IsRequired();
                entity.HasOne(version => version.Person)
                      .WithMany()
                      .HasForeignKey(version => version.PersonId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(u => u.Id);
                entity.Property(u => u.Username)
                      .IsRequired()
                      .HasMaxLength(50);
                entity.HasIndex(u => u.Username)
                      .IsUnique();
                entity.Property(u => u.Role)
                      .HasConversion<string>();
                entity.HasOne(u => u.Supervisor)
                      .WithMany(u => u.Supervisees)
                      .HasForeignKey(u => u.SupervisorId)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(u => u.Agency)
                      .WithMany()
                      .HasForeignKey(u => u.AgencyId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<Person>(entity =>
            {
                entity.HasKey(p => p.Id);
                entity.Property(p => p.Revision).IsConcurrencyToken();
                entity.Property(p => p.IsTestData).HasDefaultValue(false);
                entity.Property(p => p.FirstName)
                      .IsRequired()
                      .HasMaxLength(50);
                entity.Property(p => p.LastName)
                                      .IsRequired()
                                      .HasMaxLength(50);
                entity.Property(p => p.GuardianName).HasMaxLength(100);
                entity.Property(p => p.PhoneNumber).HasMaxLength(20);
                entity.Property(p => p.Email).HasMaxLength(254);
                entity.Property(p => p.Address).HasMaxLength(250);
                entity.Property(p => p.BillingStreet).HasMaxLength(55);
                entity.Property(p => p.BillingCity).HasMaxLength(30);
                entity.Property(p => p.BillingState).HasMaxLength(2);
                entity.Property(p => p.BillingZip).HasMaxLength(15);
                entity.Property(p => p.PrimaryCareProvider).HasMaxLength(100);
                entity.Property(p => p.HealthcareSystemName).HasMaxLength(100);
                entity.Property(p => p.CaseManagerIsDhhsRepresentative);
                entity.Property(p => p.UsesModivcare);
                entity.Property(p => p.RepPayeeMonthlyIncome).HasColumnType("decimal(18,2)");
                entity.Property(p => p.RepPayeeRegularCheckRequestNeeds)
                      .HasMaxLength(RepresentativePayeeRules.MaxRegularCheckRequestNeedsLength);

                // Encrypted SSN, declared as shadow properties on purpose.
                //
                // The columns have to exist here because this context owns the schema
                // both environments are migrated to. Nothing on the desktop may read
                // them: an SSN is cloud-only, decrypted only inside the API during an
                // audited form fill. Declaring them as shadow properties rather than
                // adding them to Person means the local path physically cannot reach a
                // consumer's SSN — there is no property to bind, project, or forget to
                // exclude from a DTO. See DECISIONS.md, "An SSN is cloud-only".
                //
                // SsnLastFour is the exception and is stored in the clear: it is what
                // the mask displays, it cannot reconstruct the number, and keeping it
                // out of the ciphertext lets every read path stay plaintext-free
                // without a Key Vault call. It is still shadow here, because the
                // desktop displays what the API hands it rather than reading the column.
                entity.Property<byte[]>("SsnCiphertext");
                entity.Property<byte[]>("SsnNonce");
                entity.Property<byte[]>("SsnTag");
                entity.Property<byte[]>("SsnWrappedKey");
                entity.Property<string>("SsnKeyId").HasMaxLength(400);
                entity.Property<string>("SsnLastFour").HasMaxLength(4);

                entity.HasOne<User>()
                      .WithMany()
                      .HasForeignKey(p => p.UserId)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(p => p.Agency)
                      .WithMany()
                      .HasForeignKey(p => p.AgencyId)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne<User>(p => p.User)
                      .WithMany()
                      .HasForeignKey(p => p.UserId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<PersonContact>(entity =>
            {
                entity.HasKey(c => c.Id);
                entity.Property(c => c.FirstName).IsRequired().HasMaxLength(75);
                entity.Property(c => c.LastName).IsRequired().HasMaxLength(75);
                entity.Property(c => c.Kind).HasConversion<string>().HasMaxLength(30);
                entity.Property(c => c.Relationship).HasMaxLength(100);
                entity.Property(c => c.Organization).HasMaxLength(150);
                entity.Property(c => c.Phone).HasMaxLength(30);
                entity.Property(c => c.Email).HasMaxLength(254);
                entity.HasIndex(c => new { c.PersonId, c.IsActive });
                entity.HasOne(c => c.Person)
                      .WithMany(p => p.Contacts)
                      .HasForeignKey(c => c.PersonId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<PersonProvider>(entity =>
            {
                entity.HasKey(link => link.Id);
                entity.Property(link => link.Role).HasMaxLength(ConsumerProviderRules.MaxRoleLength);
                entity.HasIndex(link => new { link.PersonId, link.EndDate });

                // Cascade from the person, Restrict from the provider. A consumer's records
                // go with the consumer; a directory entry someone is currently seeing may
                // not be deleted out from under them.
                entity.HasOne(link => link.Person)
                      .WithMany()
                      .HasForeignKey(link => link.PersonId)
                      .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne<Provider>()
                      .WithMany()
                      .HasForeignKey(link => link.ProviderId)
                      .OnDelete(DeleteBehavior.Restrict);

                // The at-most-one-primary-care rule and the one-current-link-per-provider
                // rule, enforced by the database as well as by the services. Both filters
                // are on EndDate IS NULL, because an ended relationship constrains nothing:
                // a consumer may have had several primary care providers over the years and
                // may return to a provider they previously left.
                entity.HasIndex(link => link.PersonId)
                      .IsUnique()
                      .HasFilter("[IsPrimaryCare] = 1 AND [EndDate] IS NULL")
                      .HasDatabaseName("IX_PersonProviders_OneCurrentPrimaryCare");
                entity.HasIndex(link => new { link.PersonId, link.ProviderId })
                      .IsUnique()
                      .HasFilter("[EndDate] IS NULL")
                      .HasDatabaseName("IX_PersonProviders_OneCurrentLinkPerProvider");
            });

            modelBuilder.Entity<Note>(entity =>
            {
                entity.HasKey(n => n.Id);
                entity.Property(n => n.Revision).IsConcurrencyToken();
                entity.Property(n => n.Narrative)
                      .IsRequired();
                entity.Property(n => n.VisitDocumentationJson);
                entity.HasOne(n => n.Person)
                      .WithMany(p => p.Notes)
                      .HasForeignKey(n => n.PersonId)
                      .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(n => n.Agency)
                      .WithMany()
                      .HasForeignKey(n => n.AgencyId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<Form>(entity =>
            {
                entity.HasKey(f => f.Id);
                entity.Property(f => f.Type)
                      .HasConversion<string>();
                entity.HasOne(f => f.Person)
                      .WithMany(p => p.Forms)
                      .HasForeignKey(f => f.PersonId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ReviewItem>(entity =>
            {
                entity.HasKey(r => r.Id);
                entity.Property(r => r.Category)
                      .HasConversion<string>()
                      .HasMaxLength(30);
                entity.HasOne(r => r.Person)
                      .WithMany()
                      .HasForeignKey(r => r.PersonId)
                      .OnDelete(DeleteBehavior.Cascade);

                // Enforces idempotent generation at the database level: one item
                // per client, cycle, quarter, category, and slot. Generation can
                // run repeatedly without the July duplicate-forms scenario,
                // because the constraint makes duplicates impossible rather than
                // merely unlikely.
                entity.HasIndex(r => new { r.PersonId, r.CycleAnchor, r.Quarter, r.Category, r.SlotIndex })
                                                      .IsUnique();
            });

            modelBuilder.Entity<Appointment>(entity =>
            {
                entity.HasKey(a => a.Id);
                entity.Property(a => a.ProviderName).HasMaxLength(100);

                // One-to-one with the Medical/Dental ReviewItem it was recorded on.
                // Appointment is the dependent (it carries ReviewItemId); WithOne +
                // HasForeignKey<Appointment> is what makes this one-to-one, and EF
                // creates the unique index on ReviewItemId automatically from that
                // configuration — so a review item owns at most one appointment. No
                // explicit HasIndex here: adding one would duplicate that index.
                // Cascade: an appointment has no record-keeping value once its item
                // (and above it, the client) is gone — the opposite of ATRequest.
                entity.HasOne(a => a.ReviewItem)
                      .WithOne(r => r.Appointment)
                      .HasForeignKey<Appointment>(a => a.ReviewItemId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ATRequest>(entity =>
            {
                entity.HasKey(a => a.Id);
                entity.Property(a => a.Revision).IsConcurrencyToken();

                // Status persisted as its enum name ("Approved"), not an ordinal.
                // Chosen for a financial record's readability and to avoid the
                // append-only fragility that int storage imposes on NoteStatus.
                entity.Property(a => a.Status)
                      .HasConversion<string>()
                      .HasMaxLength(20);

                // A rate, not an amount — decimal(5,4), matching
                // Settings.PassthroughRate, which is where this value is copied
                // from at publication. The decimal(18,2) default EF would pick for
                // a money column rounds 0.055 to 0.06, which is the difference
                // between reproducing a filed document and restating it. Nullable:
                // a draft has no frozen rate and follows the agency's current one.
                entity.Property(a => a.PassthroughRate)
                      .HasColumnType("decimal(5,4)");

                // SalesTax is decimal → SQL decimal(18,2) by default, correct for
                // currency; stated explicitly to match the Settings/ClaimLine
                // money columns rather than lean on the default.
                entity.Property(a => a.SalesTax).HasColumnType("decimal(18,2)");

                // Link to the client. Restrict, NOT cascade: a payment request is
                // a document of record and already carries snapshot columns, so it
                // must survive the client's deletion rather than vanish with it.
                // Contrast Note/Form below, which cascade because they have no
                // independent record-keeping value once the person is gone.
                entity.HasOne(a => a.Person)
                      .WithMany()
                      .HasForeignKey(a => a.PersonId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<ATRequestItem>(entity =>
            {
                entity.HasKey(i => i.Id);
                entity.Property(i => i.ItemCost).HasColumnType("decimal(18,2)");

                // Cascade: line items are worthless orphaned from their request.
                // Same parent-child shape as ClaimLine → BillingPeriod above.
                entity.HasOne(i => i.ATRequest)
                      .WithMany(a => a.Items)
                      .HasForeignKey(i => i.ATRequestId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Settings>(entity =>
            {
                entity.HasKey(s => s.Id);
                entity.Property(s => s.Revision).IsConcurrencyToken();
                entity.Property(s => s.BillingComplianceRequirements)
                      .HasConversion<int>()
                      .HasDefaultValue(Contracts.V1.BillingComplianceGate.DefaultRequirements);
                entity.HasIndex(s => s.AgencyId).IsUnique();
                entity.HasOne<Agency>()
                      .WithMany()
                      .HasForeignKey(s => s.AgencyId)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.Property(s => s.BaseIncentive).HasColumnType("decimal(18,2)");
                entity.Property(s => s.PerUnitIncentive).HasColumnType("decimal(18,2)");

                // A rate, not an amount — decimal(5,4) holds 0.1500 with room for
                // sub-percent precision, unlike the decimal(18,2) money columns
                // above. HasDefaultValue backfills the existing Settings row (and
                // writes a going-forward DEFAULT) so nothing ever computes the
                // passthrough against 0. Same rationale as HealthcareSystemsJson.
                entity.Property(s => s.PassthroughRate)
                      .HasColumnType("decimal(5,4)")
                      .HasDefaultValue(0.15m);

                // SQL-level default. When this non-nullable column is added by the
                // migration, EF needs a value for the rows already in your database;
                // HasDefaultValue supplies ["Other"] for that backfill AND writes a
                // DEFAULT constraint going forward. Without it, existing rows get ""
                // and the [NotMapped] wrapper reads that as an empty dropdown.
                entity.Property(s => s.HealthcareSystemsJson)
                                      .HasDefaultValue("""["Other"]""");

                // Rate, not amount — decimal(5,4) like PassthroughRate. HasDefaultValue
                // backfills the existing row and writes a going-forward DEFAULT so the
                // tax never computes against 0.
                entity.Property(s => s.SalesTaxRate)
                      .HasColumnType("decimal(5,4)")
                      .HasDefaultValue(0.055m);

                // FK to Provider, no nav property (Settings doesn't navigate to it;
                // the AT page loads providers separately). SetNull so deleting the
                // default provider clears the setting rather than blocking the delete.
                // Deliberately NO HasDefaultValue: a SQL default of 1 would backfill
                // the existing Settings row at AddColumn time, which can fire before
                // the Provider seed inserts row 1 — an FK violation. Nullable-null is
                // safe; the default gets set via the Settings window in slice 2.
                entity.HasOne<Provider>()
                      .WithMany()
                      .HasForeignKey(s => s.DefaultPassthroughProviderId)
                      .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<Incentive>(entity =>
            {
                entity.HasKey(i => i.Id);
                entity.Property(i => i.BaseIncentive).HasColumnType("decimal(18,2)");
                entity.Property(i => i.PerUnitIncentive).HasColumnType("decimal(18,2)");
                entity.HasIndex(i => new { i.UserId, i.Month, i.Year }).IsUnique();
                entity.HasOne(i => i.User)
                      .WithMany()
                      .HasForeignKey(i => i.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Scratchpad>(entity =>
            {
                entity.HasKey(s => s.Id);
                entity.Property(s => s.Revision).IsConcurrencyToken();
                entity.HasIndex(s => new { s.UserId, s.Date })
                      .IsUnique();
            });

            modelBuilder.Entity<ScratchpadComment>(entity =>
            {
                entity.HasKey(comment => comment.Id);
                entity.Property(comment => comment.AuthorDisplayName)
                      .IsRequired()
                      .HasMaxLength(200);
                entity.Property(comment => comment.Content)
                      .IsRequired();
                entity.HasIndex(comment => new { comment.ScratchpadId, comment.CreatedAtUtc });
                entity.HasOne(comment => comment.Scratchpad)
                      .WithMany(scratchpad => scratchpad.Comments)
                      .HasForeignKey(comment => comment.ScratchpadId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<BillingPeriod>(entity =>
            {
                entity.HasKey(b => b.Id);
                entity.Property(b => b.Status).IsConcurrencyToken();
                entity.HasOne(b => b.User)
                      .WithMany()
                      .HasForeignKey(b => b.UserId)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(b => new { b.UserId, b.Month, b.Year })
                      .IsUnique();
            });

            modelBuilder.Entity<ClaimLine>(entity =>
            {
                entity.HasKey(c => c.Id);
                entity.HasIndex(c => c.NoteId).IsUnique();
                entity.Property(c => c.Units).HasColumnType("decimal(18,2)");
                entity.Property(c => c.ChargeAmount).HasColumnType("decimal(18,2)");
                entity.HasOne(c => c.BillingPeriod)
                      .WithMany(b => b.Lines)
                      .HasForeignKey(c => c.BillingPeriodId)
                      .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(c => c.Note)
                      .WithMany()
                      .HasForeignKey(c => c.NoteId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<EdiGeneration>(entity =>
            {
                entity.HasKey(generation => generation.Id);
                entity.HasIndex(generation => new
                    { generation.AgencyId, generation.ActorUserId, generation.IdempotencyKey })
                    .IsUnique();
                entity.Property(generation => generation.IdempotencyKey)
                      .IsRequired()
                      .HasMaxLength(32);
                entity.Property(generation => generation.FileName)
                      .IsRequired()
                      .HasMaxLength(260);
                entity.Property(generation => generation.Content).IsRequired();
                entity.HasOne(generation => generation.BillingPeriod)
                      .WithMany()
                      .HasForeignKey(generation => generation.BillingPeriodId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<IncidentGroup>(entity =>
            {
                entity.HasKey(incident => incident.Id);
                entity.HasIndex(incident => new
                {
                    incident.AgencyId,
                    incident.Scope,
                    incident.Source,
                    incident.Operation,
                    incident.ExceptionFingerprint
                }).IsUnique();
                entity.HasIndex(incident => new { incident.AgencyId, incident.LastSeenUtc });
                entity.Property(incident => incident.Source).IsRequired().HasMaxLength(20);
                entity.Property(incident => incident.Scope).IsRequired().HasMaxLength(20);
                entity.Property(incident => incident.Severity).IsRequired().HasMaxLength(20);
                entity.Property(incident => incident.Operation).IsRequired().HasMaxLength(80);
                entity.Property(incident => incident.FirstRelease).IsRequired().HasMaxLength(30);
                entity.Property(incident => incident.LastRelease).IsRequired().HasMaxLength(30);
                entity.Property(incident => incident.ExceptionFingerprint).IsRequired().HasMaxLength(64);
                entity.Property(incident => incident.Status).IsRequired().HasMaxLength(20);
                entity.Property(incident => incident.LastReference).IsRequired().HasMaxLength(40);
                entity.Property(incident => incident.LastActorRole).IsRequired().HasMaxLength(30);
                entity.HasOne<Agency>()
                      .WithMany()
                      .HasForeignKey(incident => incident.AgencyId)
                      .OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}
