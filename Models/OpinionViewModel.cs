using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace MonitoringConfigurator.Models
{
    public class OpinionViewModel
    {
        [Required(ErrorMessage = "Proszê wpisaæ treœæ opinii.")]
        [MaxLength(4000)]
        [Display(Name = "Twoja opinia")]
        public string Message { get; set; } = string.Empty;

        [Range(1, 5, ErrorMessage = "Proszê zaznaczyæ ocenê (1-5 gwiazdek).")]
        [Display(Name = "Ocena")]
        public int Rating { get; set; } = 5; // Domyœlnie 5

        public IEnumerable<Contact> ExistingOpinions { get; set; } = System.Linq.Enumerable.Empty<Contact>();
    }
}