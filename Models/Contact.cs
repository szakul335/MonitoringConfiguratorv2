using System;
using System.Collections.Generic; // Dodaj to
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema; // Dodaj to

namespace MonitoringConfigurator.Models
{
    public enum ContactType
    {
        General = 0,
        Support = 1,
        Opinion = 2
    }

    public enum ContactStatus
    {
        New = 0,
        Read = 1,
        Replied = 2,
        Closed = 3
    }

    public class Contact
    {
        public int Id { get; set; }

        public string? UserId { get; set; }

        [EmailAddress]
        public string? GuestEmail { get; set; }
        public string? GuestName { get; set; }

        public ContactType Type { get; set; } = ContactType.General;
        public ContactStatus Status { get; set; } = ContactStatus.New;

        [Required, MaxLength(200)]
        public string Subject { get; set; } = string.Empty;

        [Required, MaxLength(4000)]
        public string Message { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // --- NOWE POLA DLA CZATU ---

        // ID wiadomoœci g³ównej (pierwszej w w¹tku)
        public int? ParentId { get; set; }

        [ForeignKey("ParentId")]
        public virtual Contact? Parent { get; set; }

        // Lista odpowiedzi w w¹tku
        public virtual ICollection<Contact> Replies { get; set; } = new List<Contact>();
    }
}