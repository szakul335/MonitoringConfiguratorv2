using System.Collections.Generic;

namespace MonitoringConfigurator.Models
{
    public class HomeViewModel
    {
        // Lista produktów (np. bestsellery)
        public IEnumerable<Product> FeaturedProducts { get; set; } = new List<Product>();

        // Lista opinii (np. ostatnio dodane)
        public IEnumerable<ParsedOpinion> LatestOpinions { get; set; } = new List<ParsedOpinion>();
    }
}