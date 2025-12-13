using System.ComponentModel.DataAnnotations;

namespace MonitoringConfigurator.Models
{
    public class ProductManagementViewModel : ProductCatalogViewModel
    {
        [Display(Name = "Produkt")]
        public Product EditableProduct { get; set; } = new();

        public bool IsEdit => EditableProduct.Id > 0;
    }
}
