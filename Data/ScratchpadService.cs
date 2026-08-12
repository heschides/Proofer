using Microsoft.EntityFrameworkCore;
using Sati.Models;
using System.Diagnostics;

namespace Sati.Data
{
    public class ScratchpadService : IScratchpadService
    {
        private readonly IDbContextFactory<SatiContext> _contextFactory;

        public ScratchpadService(IDbContextFactory<SatiContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public async Task<Scratchpad> LoadTodayAsync(int userId)
        {
            await using var context = _contextFactory.CreateDbContext();
            var today = DateTime.Today;
            var scratchpad = await context.Scratchpad
                .FirstOrDefaultAsync(s => s.UserId == userId && s.Date == today);

            if (scratchpad is null)
            {
                scratchpad = new Scratchpad { UserId = userId, Date = today };
                context.Scratchpad.Add(scratchpad);
                await context.SaveChangesAsync();
            }

            return scratchpad;
        }

        public async Task<List<Scratchpad>> GetHistoryAsync(int userId)
        {
            await using var context = _contextFactory.CreateDbContext();
            var today = DateTime.Today;
            return await context.Scratchpad
                .AsNoTracking()
                .Include(s => s.Comments.OrderBy(comment => comment.CreatedAtUtc))
                .Where(s => s.UserId == userId && s.Date < today)
                .OrderByDescending(s => s.Date)
                .ToListAsync();
        }

        public async Task<ScratchpadComment> AddCommentAsync(
            int scratchpadId,
            int userId,
            string authorDisplayName,
            string content)
        {
            var normalizedContent = content.Trim();
            if (string.IsNullOrWhiteSpace(normalizedContent))
                throw new ArgumentException("A retrospective comment cannot be empty.", nameof(content));

            await using var context = _contextFactory.CreateDbContext();
            var scratchpadExists = await context.Scratchpad.AnyAsync(s =>
                s.Id == scratchpadId &&
                s.UserId == userId &&
                s.Date < DateTime.Today);

            if (!scratchpadExists)
                throw new InvalidOperationException("Comments can only be added to your own past scratchpad entries.");

            var comment = new ScratchpadComment
            {
                ScratchpadId = scratchpadId,
                AuthorUserId = userId,
                AuthorDisplayName = string.IsNullOrWhiteSpace(authorDisplayName)
                    ? "Unknown user"
                    : authorDisplayName.Trim(),
                CreatedAtUtc = DateTime.UtcNow,
                Content = normalizedContent
            };

            context.ScratchpadComments.Add(comment);
            await context.SaveChangesAsync();
            return comment;
        }

        public async Task SaveAsync(Scratchpad scratchpad)
        {
            await using var context = _contextFactory.CreateDbContext();
            context.Scratchpad.Update(scratchpad);
            await context.SaveChangesAsync();
        }
    }
}
