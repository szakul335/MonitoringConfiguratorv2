using System.ComponentModel.DataAnnotations;

namespace MonitoringConfigurator.Models
{
    public class Contact
    {
        public int Id { get; set; }
        public string? UserId { get; set; }

        [Required, MaxLength(200)]
        public string Subject { get; set; } = string.Empty;

        [Required, MaxLength(4000)]
        public string Message { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
