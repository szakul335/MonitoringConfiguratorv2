using System;
using System.ComponentModel.DataAnnotations;

namespace MonitoringConfigurator.Models
{
    public class UserDocument
    {
        public int Id { get; set; }

        [Required] public string UserId { get; set; } = default!;
        [Required, MaxLength(200)] public string Title { get; set; } = "Specyfikacja konfiguracji";
        [Required, MaxLength(10)] public string Format { get; set; } = "pdf"; 
        [Required] public byte[] Content { get; set; } = default!;
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        public string? InputJson { get; set; }
        public string? ResultJson { get; set; }
    }
}
