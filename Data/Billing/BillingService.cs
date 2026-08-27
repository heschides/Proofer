using Microsoft.EntityFrameworkCore;
using Sati.Data;
using Sati.Data.Billing;
using Sati.Models;
using Sati.Models.Billing;
using Sati.Contracts.V1;

namespace Sati.Services.Billing
{
    public class BillingService : IBillingService
    {
        private readonly IDbContextFactory<SatiContext> _contextFactory;
        private readonly ISessionService _sessionService;
        private BillingComplianceRequirements _complianceRequirements =
            BillingComplianceGate.DefaultRequirements;

        public BillingService(IDbContextFactory<SatiContext> contextFactory, ISessionService sessionService)
        {
            _contextFactory = contextFactory;
            _sessionService = sessionService;
        }

        public async Task<BillingPeriod> GetOrCreateBillingPeriodAsync(int userId, int month, int year)
        {
            var actor = CurrentBillingAdministrator();
            if (month is < 1 or > 12 || year is < 2000 or > 2200)
                throw new ArgumentOutOfRangeException(nameof(month), "The billing period is invalid.");
            await using var context = _contextFactory.CreateDbContext();
            if (!await context.Users.AnyAsync(user => user.Id == userId && user.AgencyId == actor.AgencyId))
                throw new InvalidOperationException("The billing user was not found in your agency.");
            var period = await context.BillingPeriods
                .FirstOrDefaultAsync(b => b.UserId == userId
                    && b.Month == month
                    && b.Year == year);

            if (period is not null)
                return period;

            period = new BillingPeriod
            {
                UserId = userId,
                Month = month,
                Year = year,
                Status = BillingStatus.Draft
            };

            context.BillingPeriods.Add(period);
            try
            {
                await context.SaveChangesAsync();
                return period;
            }
            catch (DbUpdateException)
            {
                context.ChangeTracker.Clear();
                var completed = await context.BillingPeriods.AsNoTracking()
                    .SingleOrDefaultAsync(candidate => candidate.UserId == userId &&
                        candidate.Month == month && candidate.Year == year);
                if (completed is not null)
                    return completed;
                throw;
            }
        }

        public async Task<IEnumerable<BillingPeriod>> GetBillingPeriodsAsync(int userId)
        {
            var actor = CurrentBillingAdministrator();
            await using var context = _contextFactory.CreateDbContext();
            return await context.BillingPeriods
                .Where(period => period.UserId == userId &&
                    context.Users.Any(user => user.Id == period.UserId && user.AgencyId == actor.AgencyId))
                .Include(b => b.Lines)
                .OrderByDescending(b => b.Year)
                .ThenByDescending(b => b.Month)
                .ToListAsync();
        }

        public async Task<IEnumerable<BillingPeriod>> GetAllBillingPeriodsAsync()
        {
            var actor = CurrentBillingAdministrator();
            await using var context = _contextFactory.CreateDbContext();
            return await context.BillingPeriods
                .Where(period => context.Users.Any(user =>
                    user.Id == period.UserId && user.AgencyId == actor.AgencyId))
                .Include(b => b.Lines)
                .OrderByDescending(b => b.Year)
                .ThenByDescending(b => b.Month)
                .ToListAsync();
        }

        public async Task<ClaimLine> CreateClaimLineAsync(int noteId, bool isComplianceException = false, string? complianceExceptionReason = null)
        {
            var actor = CurrentBillingAdministrator();
            await using var context = _contextFactory.CreateDbContext();

            var note = await context.Notes
                .Include(n => n.Person)
                    .ThenInclude(p => p.Agency)
                .Include(n => n.Person)
                    .ThenInclude(p => p.Forms)
                .FirstOrDefaultAsync(n => n.Id == noteId && n.Person.AgencyId == actor.AgencyId)
                ?? throw new InvalidOperationException($"Note {noteId} was not found in your agency.");

            if (note.Person is null)
                throw new InvalidOperationException($"Note {noteId} has no associated person.");

            if (note.Status != NoteStatus.Approved)
                throw new InvalidOperationException("Only an approved service note can become a claim line.");

            _complianceRequirements = await LoadComplianceRequirementsAsync(
                context, actor.AgencyId);
            var validation = ValidateNoteForBilling(note);
            if (!validation.IsValid)
                throw new InvalidOperationException(
                    $"Note {noteId} is not ready for billing: {string.Join("; ", validation.Errors)}");

            if (await context.ClaimLines.AnyAsync(line => line.NoteId == noteId))
                throw new InvalidOperationException("This service note already has a billing claim line.");

            var serviceDate = note.EventDate!.Value.Date;
            var period = await context.BillingPeriods.SingleOrDefaultAsync(candidate =>
                candidate.UserId == note.Person.UserId && candidate.Month == serviceDate.Month &&
                candidate.Year == serviceDate.Year);
            if (period is null)
            {
                period = new BillingPeriod
                {
                    UserId = note.Person.UserId,
                    Month = serviceDate.Month,
                    Year = serviceDate.Year,
                    Status = BillingStatus.Draft
                };
                context.BillingPeriods.Add(period);
            }
            if (period.Status != BillingStatus.Draft)
                throw new InvalidOperationException("This billing period is no longer a draft.");

            var units = BillingRules.CalculateSection13Units(note.Minutes);
            var procedureCode = note.Person.Agency!.BillingProcedureCode!;
            var unitRate = note.Person.Agency.BillingUnitRate!.Value;

            var claimLine = new ClaimLine
            {
                NoteId = noteId,
                DateOfService = serviceDate,
                ProcedureCode = procedureCode,
                ProcedureModifier = note.Person.Agency.BillingModifier,
                Units = units,
                ChargeAmount = BillingRules.CalculateCharge(units, unitRate),
                ClientMaineCareId = note.Person.MaineCareId ?? string.Empty,
                RenderingProviderNpi = note.Person.Agency?.Npi ?? string.Empty,
                DiagnosisCode = note.Person.DiagnosisCode ?? string.Empty,
                PlaceOfService = (int?)note.Person.PlaceOfService ?? (int)PlaceOfService.Other,
                ClaimSnapshotJson = ProfessionalClaimSnapshotCodec.Serialize(
                    CreateClaimSnapshot(note.Person, note.Person.Agency!)),
                // A claim's exception marker is an official financial-record fact.
                // It must reflect the documented supervisor decision on the note,
                // never a value supplied by the billing caller.
                IsComplianceException = note.ComplianceOverride,
                ComplianceExceptionReason = note.ComplianceOverride ? note.OverrideReason : null
            };

            // Attach through the period's collection rather than by copying its id.
            // The first claim line of a new month is created alongside the period
            // itself, whose identity is still 0 until SaveChanges runs; assigning
            // BillingPeriodId here would persist a line pointing at no period.
            period.Lines.Add(claimLine);
            LocalAuditTrail.Record(context, actor, LocalAuditActions.BillingClaimLineCreated, "Note", noteId);
            try
            {
                await context.SaveChangesAsync();
                return claimLine;
            }
            catch (DbUpdateException)
            {
                context.ChangeTracker.Clear();
                if (await context.ClaimLines.AsNoTracking().AnyAsync(line => line.NoteId == noteId))
                    throw new InvalidOperationException("This service note already has a billing claim line.");
                throw;
            }
        }

        public async Task<IEnumerable<ClaimLine>> GetUnbilledClaimLinesAsync(int userId)
        {
            var actor = CurrentBillingAdministrator();
            await using var context = _contextFactory.CreateDbContext();
            return await context.ClaimLines
                .Include(c => c.BillingPeriod)
                .Where(c => c.BillingPeriod.UserId == userId
                    && c.BillingPeriod.Status == BillingStatus.Draft
                    && context.Users.Any(user => user.Id == c.BillingPeriod.UserId &&
                        user.AgencyId == actor.AgencyId))
                .OrderBy(c => c.DateOfService)
                .ToListAsync();
        }

        public async Task SubmitBillingPeriodAsync(int billingPeriodId)
        {
            var actor = CurrentBillingAdministrator();
            await using var context = _contextFactory.CreateDbContext();
            var period = await context.BillingPeriods.Include(candidate => candidate.Lines)
                .SingleOrDefaultAsync(candidate => candidate.Id == billingPeriodId &&
                    context.Users.Any(user => user.Id == candidate.UserId && user.AgencyId == actor.AgencyId))
                ?? throw new InvalidOperationException($"Billing period {billingPeriodId} was not found in your agency.");

            if (period.Status == BillingStatus.Submitted)
                return;

            if (period.Status != BillingStatus.Draft)
                throw new InvalidOperationException("Only draft billing periods can be submitted.");
            if (period.Lines.Count == 0)
                throw new InvalidOperationException("A billing period with no claim lines cannot be submitted.");

            period.Status = BillingStatus.Submitted;
            period.SubmittedAt = DateTime.UtcNow;
            LocalAuditTrail.Record(context, actor, LocalAuditActions.BillingPeriodSubmitted,
                "BillingPeriod", billingPeriodId);
            try
            {
                await context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                context.ChangeTracker.Clear();
                var completed = await context.BillingPeriods.AsNoTracking()
                    .SingleOrDefaultAsync(candidate => candidate.Id == billingPeriodId);
                if (completed?.Status == BillingStatus.Submitted)
                    return;
                throw new InvalidOperationException(
                    "The billing period changed while it was being submitted.");
            }
        }

        public async Task<IEnumerable<Note>> GetApprovedUnbilledNotesAsync()
        {
            var actor = CurrentBillingAdministrator();
            await using var context = _contextFactory.CreateDbContext();
            _complianceRequirements = await LoadComplianceRequirementsAsync(
                context, actor.AgencyId);
            return await context.Notes
                .Include(n => n.Person)
                    .ThenInclude(p => p.Agency)
                .Include(n => n.Person)
                    .ThenInclude(p => p.Forms)
                .Where(n => n.Status == NoteStatus.Approved
                         && n.Person.AgencyId == actor.AgencyId
                         && !context.ClaimLines.Any(c => c.NoteId == n.Id))
                .OrderBy(n => n.EventDate)
                .ToListAsync();
        }

        public BillingValidationResult ValidateNoteForBilling(Note note)
        {
            var errors = new List<string>();

            if (note.Status != NoteStatus.Approved)
                errors.Add("Service note is not approved.");

            if (note.EventDate is null)
                errors.Add("No service date.");

            if (BillingRules.CalculateSection13Units(note.Minutes) < 1)
                errors.Add("Units must be at least 1 (minimum billable unit for Section 13 TCM).");

            if (string.IsNullOrWhiteSpace(note.Person?.MaineCareId))
                errors.Add("Consumer has no MaineCare ID.");

            if (!BillingRules.IsValidDiagnosisCode(note.Person?.DiagnosisCode))
                errors.Add("Consumer diagnosis code is missing or invalid.");

            if (note.Person?.PlaceOfService is null)
                errors.Add("Consumer has no place of service.");

            if (!HasValidSubscriberClaimIdentity(note.Person))
                errors.Add("Consumer claim name, birth date, or structured claim address is incomplete or invalid.");

            if (!BillingRules.IsValidNpi(note.Person?.Agency?.Npi))
                errors.Add("Agency NPI is missing or invalid.");

            if (note.Person?.Agency is Agency agency)
                errors.AddRange(ValidateBillingConfiguration(agency));

            if (note.Person is not null && note.EventDate is not null)
            {
                if (note.ComplianceOverride)
                {
                    if (string.IsNullOrWhiteSpace(note.OverrideReason) ||
                        note.OverrideApprovedById is null || note.OverrideApprovedAt is null)
                        errors.Add("Compliance override is incomplete.");
                }
                else
                {
                    var (passed, complianceReasons) = note.Person.EvaluateComplianceGate(
                        BillingRules.MaineBusinessDate(DateTimeOffset.UtcNow),
                        requirements: _complianceRequirements);
                    if (!passed)
                        errors.AddRange(complianceReasons);
                    errors.AddRange(note.Person.EvaluateBillingWindow(
                        note.EventDate.Value, _complianceRequirements));
                }
            }

            return new BillingValidationResult(
                IsValid: errors.Count == 0,
                Note: note,
                Errors: errors);
        }

        public async Task<BillingConfiguration> GetBillingConfigurationAsync()
        {
            var actor = CurrentBillingAdministrator();
            await using var context = _contextFactory.CreateDbContext();
            var agency = await context.Agencies.AsNoTracking()
                .SingleAsync(candidate => candidate.Id == actor.AgencyId);
            return ToBillingConfiguration(agency);
        }

        private static async Task<BillingComplianceRequirements> LoadComplianceRequirementsAsync(
            SatiContext context,
            int agencyId) =>
            await context.Settings.AsNoTracking()
                .Where(settings => settings.AgencyId == agencyId)
                .Select(settings => (BillingComplianceRequirements?)settings.BillingComplianceRequirements)
                .SingleOrDefaultAsync() ?? BillingComplianceGate.DefaultRequirements;

        public async Task SaveBillingConfigurationAsync(BillingConfiguration configuration)
        {
            var actor = CurrentBillingAdministrator();
            var normalized = NormalizeBillingConfiguration(configuration);
            var errors = ValidateBillingConfiguration(normalized);
            if (errors.Count > 0)
                throw new ArgumentException(string.Join(" ", errors), nameof(configuration));

            await using var context = _contextFactory.CreateDbContext();
            var agency = await context.Agencies.SingleAsync(candidate => candidate.Id == actor.AgencyId);
            agency.BillingProcedureCode = normalized.ProcedureCode;
            agency.BillingModifier = normalized.Modifier;
            agency.BillingUnitRate = normalized.UnitRate;
            agency.EdiSubmitterId = normalized.EdiSubmitterId;
            agency.EdiPayerName = normalized.PayerName;
            agency.EdiPayerId = normalized.PayerId;
            agency.EdiContactName = normalized.ContactName;
            agency.EdiContactPhone = normalized.ContactPhone;
            LocalAuditTrail.Record(context, actor, LocalAuditActions.BillingConfigurationUpdated,
                "Agency", agency.Id);
            await context.SaveChangesAsync();
        }

        private static BillingConfiguration ToBillingConfiguration(Agency agency) => new(
            agency.BillingProcedureCode ?? string.Empty,
            agency.BillingModifier,
            agency.BillingUnitRate,
            agency.EdiSubmitterId ?? string.Empty,
            agency.EdiPayerName ?? string.Empty,
            agency.EdiPayerId ?? string.Empty,
            agency.EdiContactName ?? string.Empty,
            agency.EdiContactPhone ?? string.Empty);

        private static BillingConfiguration NormalizeBillingConfiguration(BillingConfiguration configuration) => new(
            (configuration.ProcedureCode ?? string.Empty).Trim().ToUpperInvariant(),
            string.IsNullOrWhiteSpace(configuration.Modifier) ? null : configuration.Modifier.Trim().ToUpperInvariant(),
            configuration.UnitRate,
            (configuration.EdiSubmitterId ?? string.Empty).Trim(),
            (configuration.PayerName ?? string.Empty).Trim(),
            (configuration.PayerId ?? string.Empty).Trim(),
            (configuration.ContactName ?? string.Empty).Trim(),
            new string((configuration.ContactPhone ?? string.Empty).Where(char.IsDigit).ToArray()));

        private static IReadOnlyList<string> ValidateBillingConfiguration(Agency agency)
        {
            var errors = ValidateBillingConfiguration(ToBillingConfiguration(agency)).ToList();
            if (!BillingRules.IsSafeX12Element(agency.Name, 60) ||
                !BillingRules.IsValidNpi(agency.Npi) ||
                !BillingRules.IsSafeX12Element(agency.TaxId, 50) ||
                !BillingRules.IsSafeX12Element(agency.Street, 55) ||
                !BillingRules.IsSafeX12Element(agency.City, 30) ||
                !BillingRules.IsSafeX12Element(agency.State, 2) ||
                !BillingRules.IsSafeX12Element(agency.Zip, 15))
                errors.Add("Agency billing provider name, NPI, tax ID, or structured address is incomplete or invalid.");
            return errors;
        }

        private static IReadOnlyList<string> ValidateBillingConfiguration(BillingConfiguration configuration)
        {
            var errors = new List<string>();
            if (!BillingRules.IsValidProcedureCode(configuration.ProcedureCode))
                errors.Add("Agency billing procedure code is missing or invalid.");
            if (!BillingRules.IsValidModifier(configuration.Modifier))
                errors.Add("Agency billing modifier is invalid.");
            if (configuration.UnitRate is null or <= 0)
                errors.Add("Agency billing unit rate is missing or invalid.");
            if (!BillingRules.IsSafeX12Element(configuration.EdiSubmitterId, 15))
                errors.Add("EDI submitter ID is missing, invalid, or longer than 15 characters.");
            if (!BillingRules.IsSafeX12Element(configuration.PayerName, 60) ||
                !BillingRules.IsSafeX12Element(configuration.PayerId, 80))
                errors.Add("EDI payer name or payer ID is missing or invalid.");
            if (!BillingRules.IsSafeX12Element(configuration.ContactName, 60) ||
                configuration.ContactPhone.Length is < 10 or > 15 ||
                configuration.ContactPhone.Any(character => !char.IsDigit(character)))
                errors.Add("EDI contact name or telephone number is missing or invalid.");
            return errors;
        }

        private static bool HasValidSubscriberClaimIdentity(Person? person) =>
            person is not null &&
            BillingRules.IsSafeX12Element(person.FirstName, 35) &&
            BillingRules.IsSafeX12Element(person.LastName, 60) &&
            person.BirthDate >= new DateTime(1900, 1, 1) &&
            BillingRules.IsSafeX12Element(person.BillingStreet, 55) &&
            BillingRules.IsSafeX12Element(person.BillingCity, 30) &&
            BillingRules.IsSafeX12Element(person.BillingState, 2) &&
            BillingRules.IsSafeX12Element(person.BillingZip, 15);

        private static ProfessionalClaimSnapshot CreateClaimSnapshot(Person person, Agency agency) => new(
            ProfessionalClaimSnapshotCodec.CurrentVersion,
            agency.Id,
            person.Id,
            person.FirstName!,
            person.LastName!,
            person.BirthDate.Date,
            person.Gender == Gender.Male ? "M" : person.Gender == Gender.Female ? "F" : "U",
            person.MaineCareId!,
            person.BillingStreet!,
            person.BillingCity!,
            person.BillingState!,
            person.BillingZip!,
            agency.Name,
            agency.Npi!,
            agency.TaxId!,
            agency.Street!,
            agency.City!,
            agency.State!,
            agency.Zip!,
            agency.EdiSubmitterId!,
            agency.EdiContactName!,
            agency.EdiContactPhone!,
            agency.EdiPayerName!,
            agency.EdiPayerId!);

        private User CurrentBillingAdministrator()
        {
            var actor = _sessionService.CurrentUser
                ?? throw new UnauthorizedAccessException("A signed-in billing administrator is required.");
            if (actor.Role != UserRole.Admin)
                throw new UnauthorizedAccessException("Only an administrator may manage billing.");
            return actor;
        }
    }
}
