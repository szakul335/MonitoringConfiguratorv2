using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace MonitoringConfigurator.Models
{
    public class SupportViewModel
    {
        [Required]
        [RegularExpression("Admin|Operator")]
        public string Recipient { get; set; } = "Admin";

        [Required, MaxLength(200)]
        public string Subject { get; set; } = string.Empty;

        [Required, MaxLength(4000)]
        public string Message { get; set; } = string.Empty;

        public IEnumerable<Contact> Sent { get; set; } = System.Linq.Enumerable.Empty<Contact>();
    }
}
