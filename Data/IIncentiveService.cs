using Sati.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Sati.Data
{
    public interface IIncentiveService
    {
        Task<(Incentive incentive, bool wasCreated)> GetOrCreateAsync(int userId, int month, int year);
        Task SaveAsync(Incentive incentive);

        Task<int> GetRemainingEligibleDaysAsync(int month, int year, HashSet<DateTime> daysAlreadyWorked, HashSet<DateTime> exemptDates);

        // Read-only scheduled-day count for a selected statistics window. The
        // caller subtracts user-entered ExemptDates separately.
        Task<int> GetEligibleDaysAsync(DateTime startInclusive, DateTime endInclusive);

        // Read-only: every existing Incentive snapshot for the user, untouched.
        // Creates and mutates nothing — unlike GetOrCreateAsync, which is unsafe for reading history.
        Task<List<Incentive>> GetHistoryAsync(int userId);
    }
}
