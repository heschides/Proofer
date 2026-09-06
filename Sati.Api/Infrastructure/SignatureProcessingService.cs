using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Sati.Api.Data;
using Sati.Signatures;

namespace Sati.Api.Infrastructure;

/// <summary>Server-only recovery loop. No public-portal credentials can resolve these workers.</summary>
internal sealed class SignatureProcessingService(IDbContextFactory<ApiDbContext> factory, SignatureFeature feature,
    SignatureOptions options, SignatureCompletionWorker packages, SignatureMailWorker mail,
    ILogger<SignatureProcessingService> logger) : BackgroundService
{
    private int lastCompletion;
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (!feature.Enabled || !options.WorkersEnabled) continue;
            try { await PreparePackages(stoppingToken); }
            catch (Exception error) when (error is not OperationCanceledException)
            { logger.LogWarning("Signature package scan failed ({FailureType}). Review signing service health.", error.GetType().Name); }
            for (var i = 0; i < 10; i++)
            {
                try
                {
                    await using var db = await factory.CreateDbContextAsync(stoppingToken);
                    if (!await new SignatureStaffSingleAttempt(db).ExecuteAsync(() => mail.ProcessNextAsync(db, stoppingToken))) break;
                }
                catch (Exception error) when (error is not OperationCanceledException)
                { logger.LogWarning("Signature notification processing failed ({FailureType}). Review signing service health.", error.GetType().Name); break; }
            }
        }
    }
    private async Task PreparePackages(CancellationToken ct)
    {
        int[] candidates;
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            candidates = await db.SignatureCompletions.AsNoTracking().Where(x => x.Id > lastCompletion && !db.SignaturePackages.Any(p => p.CompletionId == x.Id))
                .OrderBy(x => x.Id).Select(x => x.Id).Take(10).ToArrayAsync(ct);
        }
        if (candidates.Length == 0) { lastCompletion = 0; return; }
        foreach (var id in candidates)
        {
            lastCompletion = id; // A damaged earlier record cannot starve every later signer of a copy.
            try
            {
                await using var db = await factory.CreateDbContextAsync(ct);
                await new SignatureStaffSingleAttempt(db).ExecuteAsync(() => packages.BuildAsync(db, id, ct));
            }
            catch (Exception error) when (error is not OperationCanceledException)
            { logger.LogWarning("Signature copy preparation failed ({FailureType}). Review signing service health.", error.GetType().Name); }
        }
    }
}
