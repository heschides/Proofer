using Sati.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Sati.Data
{
    public interface IScratchpadService
    {
        Task<Scratchpad> LoadTodayAsync(int userId);
        Task<Scratchpad> LoadTomorrowAsync(int userId);
        Task<List<Scratchpad>> GetHistoryAsync(int userId);
        Task<ScratchpadComment> AddCommentAsync(
            int scratchpadId,
            int userId,
            string authorDisplayName,
            string content);

        Task SaveAsync(Scratchpad scratchpad);
    }
}
