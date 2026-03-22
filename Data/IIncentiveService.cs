using Sati.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Sati.Data
{
    public interface IIncentiveService
    {
        Task<Incentive> GetOrCreateAsync(int userId, int month, int year);
        Task SaveAsync(Incentive incentive);
    }
}
