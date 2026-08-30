using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

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
        public int UnitsPerDay { get; set; }
        public string ExcludedDatesJson { get; set; } = "[]";

        [NotMapped]
        public List<DateTime> ExcludedDates
        {
            get => JsonSerializer.Deserialize<List<DateTime>>(ExcludedDatesJson) ?? [];
            set => ExcludedDatesJson = JsonSerializer.Serialize(value);
        }
        public int Threshold => DaysScheduled * UnitsPerDay;

        public decimal Calculate(decimal loggedUnits)
        {
            if (loggedUnits < Threshold) return 0;
            if (loggedUnits == Threshold) return BaseIncentive;
            return BaseIncentive + ((loggedUnits - Threshold) * PerUnitIncentive);
        }
    }
}