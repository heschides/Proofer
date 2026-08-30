using Sati.Models;

namespace Sati.Models.Billing
{
    public class BillingPeriod
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public int Month { get; set; }
        public int Year { get; set; }
        public BillingStatus Status { get; set; }
        public DateTime? SubmittedAt { get; set; }

        public User User { get; set; } = null!;
        public ICollection<ClaimLine> Lines { get; set; } = new List<ClaimLine>();
    }


}