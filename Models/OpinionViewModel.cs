using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace MonitoringConfigurator.Models
{
    public class OpinionViewModel
    {
        // --- Formularz dodawania ---
        [Required(ErrorMessage = "Proszê wpisaæ treœæ opinii.")]
        [MaxLength(4000)]
        [Display(Name = "Twoja opinia")]
        public string Message { get; set; } = string.Empty;

        [Range(1, 5, ErrorMessage = "Proszê zaznaczyæ ocenê.")]
        [Display(Name = "Ocena")]
        public int Rating { get; set; } = 5;

        // --- Lista i Statystyki ---
        public List<ParsedOpinion> Reviews { get; set; } = new();

        public double AverageRating { get; set; }
        public int TotalCount { get; set; }
        // Tablica na histogram (indeksy 1-5 to liczby gwiazdek)
        public int[] StarCounts { get; set; } = new int[6];

        public string CurrentSort { get; set; } = "newest";
    }

    public class ParsedOpinion
    {
        public int Id { get; set; }
        public string? UserId { get; set; }

        // To pole bêdzie zawieraæ Imiê i Nazwisko lub E-mail
        public string UserName { get; set; } = "Goœæ";

        public string Message { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public int Stars { get; set; }
        public string Initials { get; set; } = "U";
        public string? AvatarUrl { get; set; }
    }
}