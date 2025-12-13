using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace MonitoringConfigurator.Models
{
    public class Order
    {
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        public DateTime OrderDate { get; set; } = DateTime.UtcNow;

        [Display(Name = "Łączna kwota")]
        public decimal TotalAmount { get; set; }

        public List<OrderDetail> Items { get; set; } = new();
    }
}