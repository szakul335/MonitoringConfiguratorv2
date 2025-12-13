using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace MonitoringConfigurator.Models
{
    public class ProductCatalogViewModel
    {
        public IEnumerable<Product> Products { get; set; } = new List<Product>();

        [Display(Name = "Kategoria")]
        public ProductCategory? Category { get; set; }

        [Display(Name = "Szukaj")]
        public string? Query { get; set; }

        // --- Nowe pola filtrów ---
        [Display(Name = "Cena od")]
        public decimal? MinPrice { get; set; }

        [Display(Name = "Cena do")]
        public decimal? MaxPrice { get; set; }

        [Display(Name = "Min. rozdzielczoœæ")]
        public int? MinResolution { get; set; }

        [Display(Name = "Tylko zewnêtrzne")]
        public bool OutdoorOnly { get; set; }

        [Display(Name = "Sortowanie")]
        public string? SortBy { get; set; } // np. "price_asc", "price_desc"
    }
}