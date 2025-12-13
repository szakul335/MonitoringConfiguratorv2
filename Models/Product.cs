using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Http;

namespace MonitoringConfigurator.Models
{
    public enum ProductCategory
    {
        [Display(Name = "Kamera")]
        Camera = 0,
        [Display(Name = "Rejestrator")]
        Recorder = 1,
        [Display(Name = "Switch")]
        Switch = 2,
        [Display(Name = "Okablowanie")]
        Cable = 3,
        [Display(Name = "Dysk twardy")]
        Disk = 4,
        [Display(Name = "Zasilanie awaryjne (UPS)")]
        Ups = 5,
        [Display(Name = "Akcesoria")]
        Accessory = 6
    }

    public class Product
    {
        public int Id { get; set; }

        [Required]
        [StringLength(200)]
        [Display(Name = "Nazwa")]
        public string Name { get; set; } = string.Empty;

        [StringLength(100)]
        [Display(Name = "Marka")]
        public string? Brand { get; set; }

        [StringLength(100)]
        [Display(Name = "Model")]
        public string? Model { get; set; }

        [Required]
        [Display(Name = "Kategoria")]
        public ProductCategory Category { get; set; }

        [Display(Name = "Cena (PLN)")]
        [DataType(DataType.Currency)]
        [Range(0, double.MaxValue, ErrorMessage = "Cena musi być nieujemna.")]
        public decimal Price { get; set; }

        [StringLength(300)]
        [Display(Name = "Krótki opis")]
        public string? ShortDescription { get; set; }

        [StringLength(2000)]
        public string? Description { get; set; }

        [Display(Name = "Szczegółowy opis")]
        public string? LongDescription { get; set; }

        [Url]
        [Display(Name = "Link do zdjęcia")]
        public string? ImageUrl { get; set; }

        // --- NOWE POLE DO UPLOADU ---
        [NotMapped]
        [Display(Name = "Wgraj zdjęcie (JPG/PNG)")]
        public IFormFile? ImageUpload { get; set; }
        // ----------------------------

        [Display(Name = "Rozdzielczość (Mpix)")]
        public int? ResolutionMp { get; set; }

        [Display(Name = "Obiektyw")]
        public string? Lens { get; set; }

        [Display(Name = "Zasięg IR (m)")]
        public int? IrRangeM { get; set; }

        [Display(Name = "Zewn. (Outdoor)")]
        public bool? Outdoor { get; set; }

        [Display(Name = "Liczba kanałów")]
        public int? Channels { get; set; }

        [Display(Name = "Przepustowość (Mbps)")]
        public int? MaxBandwidthMbps { get; set; }

        [Display(Name = "Liczba portów")]
        public int? Ports { get; set; }

        [Display(Name = "Budżet PoE (W)")]
        public int? PoeBudgetW { get; set; }

        [Display(Name = "Zatoki dysków")]
        public int? DiskBays { get; set; }

        [Display(Name = "Maks. pojemność dysku (TB)")]
        public int? MaxHddTB { get; set; }

        [Display(Name = "Pojemność (TB)")]
        public double? StorageTB { get; set; }

        [Display(Name = "Obsługa RAID")]
        public bool? SupportsRaid { get; set; }

        [Display(Name = "Długość rolki (m)")]
        public int? RollLengthM { get; set; }

        [Display(Name = "Moc (VA)")]
        public int? UpsVA { get; set; }
    }
}