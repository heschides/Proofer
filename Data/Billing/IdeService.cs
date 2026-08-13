using Microsoft.EntityFrameworkCore;
using Sati.Data;
using Sati.Helpers;
using Sati.Models.Billing;
using System.IO;

namespace Sati.Edi
{
    public class EdiService : IEdiService
    {
        private readonly IDbContextFactory<SatiContext> _contextFactory;
        private readonly ISessionService _sessionService;

        // User-scoped output avoids requiring administrator rights or a machine-global folder.
        private static readonly string OutputDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Sati", "EDI");

        public EdiService(IDbContextFactory<SatiContext> contextFactory, ISessionService sessionService)
        {
            _contextFactory = contextFactory;
            _sessionService = sessionService;
        }

        public async Task<string> GenerateAndSaveAsync(int billingPeriodId, bool isTest, string idempotencyKey)
        {
            if (!Guid.TryParse(idempotencyKey, out var parsedKey))
                throw new ArgumentException("A valid EDI idempotency key is required.", nameof(idempotencyKey));
            var normalizedKey = parsedKey.ToString("N");
            var actor = _sessionService.CurrentUser
                ?? throw new InvalidOperationException("An authenticated user is required to generate EDI.");
            await using var context = _contextFactory.CreateDbContext();

            var previous = await context.EdiGenerations.AsNoTracking().SingleOrDefaultAsync(generation =>
                generation.AgencyId == actor.AgencyId && generation.ActorUserId == actor.Id &&
                generation.IdempotencyKey == normalizedKey);
            if (previous is not null)
            {
                if (previous.BillingPeriodId != billingPeriodId || previous.IsTest != isTest)
                    throw new InvalidOperationException("This EDI retry key was already used for a different request.");
                return await SaveFileAsync(previous.FileName, previous.Content);
            }

            var period = await context.BillingPeriods
                .Include(p => p.User)
                .Include(p => p.Lines)
                .FirstOrDefaultAsync(p => p.Id == billingPeriodId)
                ?? throw new InvalidOperationException(
                    $"Billing period {billingPeriodId} not found.");

            if (period.User.AgencyId != actor.AgencyId)
                throw new InvalidOperationException($"Billing period {billingPeriodId} not found.");

            if (!period.Lines.Any())
                throw new InvalidOperationException(
                    $"Billing period {billingPeriodId} has no claim lines.");
            if (period.Status != BillingStatus.Submitted)
                throw new InvalidOperationException(
                    "Submit and lock the billing period before generating its 837P file.");

            var generatedAt = DateTime.Now;
            var controlNumber = CreateEdiControlNumber(normalizedKey);
            var ediContent = EdiGenerator.Generate(
                period, isTest, generatedAt, controlNumber);

            // File naming per OA companion guide:
            // - Must contain OATEST for test files
            // - Must contain 837P for SFTP submissions
            // The web portal upload doesn't require 837P in the name but
            // including it is harmless and makes the file self-documenting.
            var timestamp = generatedAt.ToString("yyyyMMdd_HHmmss");
            var testMarker = isTest ? ".OATEST" : string.Empty;
            var fileName = $"837P{testMarker}_{period.Year}{period.Month:D2}_{timestamp}_{normalizedKey[..8]}.txt";
            context.EdiGenerations.Add(new EdiGeneration
            {
                AgencyId = actor.AgencyId,
                ActorUserId = actor.Id,
                BillingPeriodId = billingPeriodId,
                IdempotencyKey = normalizedKey,
                IsTest = isTest,
                FileName = fileName,
                Content = ediContent,
                CreatedAtUtc = DateTime.UtcNow
            });
            LocalAuditTrail.Record(context, actor, LocalAuditActions.BillingEdiGenerated,
                "BillingPeriod", billingPeriodId);
            try
            {
                await context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                context.ChangeTracker.Clear();
                var completed = await context.EdiGenerations.AsNoTracking().SingleOrDefaultAsync(generation =>
                    generation.AgencyId == actor.AgencyId && generation.ActorUserId == actor.Id &&
                    generation.IdempotencyKey == normalizedKey);
                if (completed is null)
                    throw;
                if (completed.BillingPeriodId != billingPeriodId || completed.IsTest != isTest)
                {
                    throw new InvalidOperationException(
                        "This EDI retry key was already used for a different request.");
                }
                return await SaveFileAsync(completed.FileName, completed.Content);
            }
            return await SaveFileAsync(fileName, ediContent);
        }

        private static string CreateEdiControlNumber(string normalizedKey) =>
            (Convert.ToUInt32(normalizedKey[..8], 16) % 1_000_000_000)
            .ToString("D9", System.Globalization.CultureInfo.InvariantCulture);

        private static async Task<string> SaveFileAsync(string fileName, string content)
        {
            Directory.CreateDirectory(OutputDirectory);
            var filePath = Path.Combine(OutputDirectory, fileName);
            await File.WriteAllTextAsync(filePath, content);
            return filePath;
        }
    }
}
