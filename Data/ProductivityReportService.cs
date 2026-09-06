using Microsoft.EntityFrameworkCore;
using Sati.Models;

namespace Sati.Data;

/// <summary>
/// Local-production implementation of the lightweight Statistics read. It
/// projects only date and minutes; note narratives never leave SQL Server.
/// </summary>
public sealed class ProductivityReportService(
    IDbContextFactory<SatiContext> contextFactory,
    ISessionService sessionService) : IProductivityReportService
{
    public async Task<IReadOnlyList<ProductivityMonthUnits>> GetUnitsAsync(
        DateTime windowStart,
        DateTime windowEnd)
    {
        var start = windowStart.Date;
        var end = windowEnd.Date;
        ValidateWindow(start, end);

        var actor = sessionService.CurrentUser
            ?? throw new UnauthorizedAccessException("A signed-in user is required.");
        await using var context = contextFactory.CreateDbContext();

        var endExclusive = end.AddDays(1);
        var rows = await context.Notes
            .AsNoTracking()
            .Where(note => note.Person.UserId == actor.Id &&
                           note.Person.AgencyId == actor.AgencyId &&
                           note.AgencyId == actor.AgencyId &&
                           note.EventDate.HasValue &&
                           note.EventDate.Value >= start &&
                           note.EventDate.Value < endExclusive &&
                           (note.Status == NoteStatus.Logged ||
                            note.Status == NoteStatus.Approved))
            .Select(note => new
            {
                EventDate = note.EventDate!.Value,
                note.Minutes
            })
            .ToListAsync();

        return rows
            .GroupBy(row => (row.EventDate.Year, row.EventDate.Month))
            .OrderBy(group => group.Key.Year)
            .ThenBy(group => group.Key.Month)
            .Select(group => new ProductivityMonthUnits(
                group.Key.Year,
                group.Key.Month,
                group.Sum(row => Note.CalculateUnits(row.Minutes) ?? 0)))
            .ToList();
    }

    private static void ValidateWindow(DateTime start, DateTime end)
    {
        if (end < start || start.Year < 2000 || end.Year > 2200 ||
            (end - start).TotalDays > 3_660)
        {
            throw new ArgumentException(
                "The productivity window must be valid, within 2000-2200, and no longer than 10 years.");
        }
    }
}
