using Microsoft.EntityFrameworkCore;
using Sati.Models;

namespace Sati.Data
{
    public class ExemptDateService : IExemptDateService
    {
        private readonly IDbContextFactory<SatiContext> _contextFactory;
        private readonly ISessionService _sessionService;

        public ExemptDateService(
            IDbContextFactory<SatiContext> contextFactory,
            ISessionService sessionService)
        {
            _contextFactory = contextFactory;
            _sessionService = sessionService;
        }

        public async Task<List<ExemptDate>> GetByYearAsync(int userId, int year)
        {
            EnsureCurrentUser(userId);
            await using var context = _contextFactory.CreateDbContext();
            return await context.ExemptDates
                .Where(e => e.UserId == userId && e.Date.Year == year)
                .OrderBy(e => e.Date)
                .ToListAsync();
        }

        public async Task<ExemptDate> AddAsync(int userId, DateTime date, string? reason = null)
        {
            EnsureCurrentUser(userId);
            await using var context = _contextFactory.CreateDbContext();
            var exemptDate = new ExemptDate
            {
                UserId = userId,
                Date = date.Date, // strip time component
                Reason = reason
            };
            context.ExemptDates.Add(exemptDate);
            await context.SaveChangesAsync();
            return exemptDate;
        }

        public async Task RemoveAsync(int id)
        {
            var actor = CurrentActor();
            await using var context = _contextFactory.CreateDbContext();
            var exemptDate = await context.ExemptDates.FindAsync(id);
            if (exemptDate is null) return;
            if (exemptDate.UserId != actor.Id)
                throw new UnauthorizedAccessException(
                    "You may change exempt dates only on your own calendar.");
            context.ExemptDates.Remove(exemptDate);
            await context.SaveChangesAsync();
        }

        private void EnsureCurrentUser(int userId)
        {
            if (CurrentActor().Id != userId)
                throw new UnauthorizedAccessException(
                    "You may access exempt dates only on your own calendar.");
        }

        private User CurrentActor() => _sessionService.CurrentUser
            ?? throw new UnauthorizedAccessException("A signed-in user is required.");
    }
}
