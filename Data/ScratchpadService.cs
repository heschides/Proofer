using Microsoft.EntityFrameworkCore;
using Sati.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Sati.Data
{
    public class ScratchpadService : IScratchpadService
    {
        private readonly SatiContext _context;

        public ScratchpadService(SatiContext context)
        {
            _context = context;
        }

        public async Task<Scratchpad> LoadTodayAsync(int userId)
        {
            var today = DateTime.Today;
            var scratchpad = await _context.Scratchpad
                .FirstOrDefaultAsync(s => s.UserId == userId && s.Date == today);

            if (scratchpad is null)
            {
                scratchpad = new Scratchpad { UserId = userId, Date = today };
                Debug.WriteLine($"SERVICE SaveAsync called with: '{scratchpad.Content}'");
                _context.Scratchpad.Add(scratchpad);
                await _context.SaveChangesAsync();
            }

            return scratchpad;
        }

        public async Task<List<Scratchpad>> GetHistoryAsync(int userId)
        {
            var today = DateTime.Today;
            return await _context.Scratchpad
                .Where(s => s.UserId == userId && s.Date < today)
                .OrderByDescending(s => s.Date)
                .ToListAsync();
        }

        public async Task SaveAsync(Scratchpad scratchpad)
        {
            _context.Scratchpad.Update(scratchpad);
            await _context.SaveChangesAsync();
        }
    }
}
