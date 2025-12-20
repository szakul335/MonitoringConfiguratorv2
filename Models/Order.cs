using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace MonitoringConfigurator.Models
{
    public enum OrderStatus
    {
        [Display(Name = "Nowe")]
        Nowe,

        [Display(Name = "W trakcie weryfikacji")]
        WTrakcie,

        [Display(Name = "Zatwierdzone (Oferta)")]
        Zatwierdzone,

        [Display(Name = "Zrealizowane")]
        Zrealizowane,

        [Display(Name = "Anulowane")]
        Anulowane
    }

    public class Order
    {
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        public DateTime OrderDate { get; set; } = DateTime.UtcNow;

        [Display(Name = "Szacunkowa kwota")]
        public decimal TotalAmount { get; set; }

        [Display(Name = "Status")]
        public OrderStatus Status { get; set; } = OrderStatus.Nowe;

        public List<OrderDetail> Items { get; set; } = new();
    }
}