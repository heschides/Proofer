namespace Sati.Models
{
    public class ExemptDate
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public DateTime Date { get; set; }
        public string? Reason { get; set; }
        public User User { get; set; } = null!;
    }
}