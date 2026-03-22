using System;
using System.Collections.Generic;
using System.Text;

namespace Sati.Models
{
    public class Incentive
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public int Month { get; set; }
        public int Year { get; set; }
        public int DaysScheduled { get; set; }
        public decimal BaseIncentive { get; set; }
        public decimal PerUnitIncentive { get; set; }

        public User User { get; set; } = null!;

        public int Threshold => DaysScheduled * 19;

        public decimal Calculate(int loggedUnits)
        {
            if (loggedUnits < Threshold) return 0;
            if (loggedUnits == Threshold) return BaseIncentive;
            return BaseIncentive + ((loggedUnits - Threshold) * PerUnitIncentive);
        }
    }
}