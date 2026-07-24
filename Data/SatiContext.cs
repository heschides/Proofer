using Microsoft.EntityFrameworkCore;
using Sati.Models;
using Sati.Models.Billing;

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
        public DbSet<Incentive> Incentives { get; set; }
        public DbSet<BillingPeriod> BillingPeriods { get; set; }
        public DbSet<ClaimLine> ClaimLines { get; set; }
        public DbSet<ExemptDate> ExemptDates { get; set; }
        public DbSet<ReviewItem> ReviewItems { get; set; }


        public SatiContext(DbContextOptions<SatiContext> options) : base(options)
        {
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
                entity.HasData(
                    new Agency { Id = 1, Name = "Internal" },
                    new Agency { Id = 2, Name = "Sandbox Mode" });
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
                entity.Property(p => p.FirstName)
                      .IsRequired()
                      .HasMaxLength(50);
                entity.Property(p => p.LastName)
                                      .IsRequired()
                                      .HasMaxLength(50);
                entity.Property(p => p.GuardianName).HasMaxLength(100);
                entity.Property(p => p.PhoneNumber).HasMaxLength(20);
                entity.Property(p => p.Address).HasMaxLength(250);
                entity.Property(p => p.PrimaryCareProvider).HasMaxLength(100);
                entity.Property(p => p.HealthcareSystemName).HasMaxLength(100); entity.HasOne<User>()
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

            modelBuilder.Entity<Note>(entity =>
            {
                entity.HasKey(n => n.Id);
                entity.Property(n => n.Narrative)
                      .IsRequired();
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

            modelBuilder.Entity<Settings>(entity =>
            {
                entity.HasKey(s => s.Id);
                entity.Property(s => s.BaseIncentive).HasColumnType("decimal(18,2)");
                entity.Property(s => s.PerUnitIncentive).HasColumnType("decimal(18,2)");

                // SQL-level default. When this non-nullable column is added by the
                // migration, EF needs a value for the rows already in your database;
                // HasDefaultValue supplies ["Other"] for that backfill AND writes a
                // DEFAULT constraint going forward. Without it, existing rows get ""
                // and the [NotMapped] wrapper reads that as an empty dropdown.
                entity.Property(s => s.HealthcareSystemsJson)
                      .HasDefaultValue("""["Other"]""");
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
                entity.HasIndex(s => new { s.UserId, s.Date })
                      .IsUnique();
            });

            modelBuilder.Entity<BillingPeriod>(entity =>
            {
                entity.HasKey(b => b.Id);
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
                entity.Property(c => c.Units).HasColumnType("decimal(18,2)");
                entity.HasOne(c => c.BillingPeriod)
                      .WithMany(b => b.Lines)
                      .HasForeignKey(c => c.BillingPeriodId)
                      .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(c => c.Note)
                      .WithMany()
                      .HasForeignKey(c => c.NoteId)
                      .OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}