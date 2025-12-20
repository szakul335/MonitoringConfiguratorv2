using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MonitoringConfigurator.Data;
using MonitoringConfigurator.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace MonitoringConfigurator.Controllers
{
    public class HomeController : Controller
    {
        private readonly AppDbContext _ctx;

        public HomeController(AppDbContext ctx)
        {
            _ctx = ctx;
        }

        public async Task<IActionResult> Index()
        {
            // 1. POBIERANIE PRODUKTÓW (np. 4 najnowsze lub wybrane)
            var products = await _ctx.Products
                .OrderByDescending(p => p.Id) // Mo¿esz zmieniæ sortowanie
                .Take(4) // Pobieramy 4 sztuki do sekcji "Polecane"
                .AsNoTracking()
                .ToListAsync();

            // 2. POBIERANIE OPINII (Logika skopiowana z InfoController, ograniczona do 3 sztuk)
            var opinionsQuery = from c in _ctx.Contacts
                                where c.Subject.StartsWith("Ocena:") || c.Subject == "Opinia o aplikacji"

                                // Do³¹czamy u¿ytkownika
                                join u in _ctx.Users on c.UserId equals u.Id into users
                                from user in users.DefaultIfEmpty()

                                    // Do³¹czamy Claim z imieniem
                                join cl in _ctx.Set<IdentityUserClaim<string>>()
                                    on new { UserId = user.Id, ClaimType = "profile:fullName" }
                                    equals new { UserId = cl.UserId, ClaimType = cl.ClaimType } into claims
                                from claim in claims.DefaultIfEmpty()

                                orderby c.CreatedAt descending
                                select new
                                {
                                    c.Id,
                                    c.UserId,
                                    c.Subject,
                                    c.Message,
                                    c.CreatedAt,
                                    Email = user != null ? user.Email : null,
                                    FullName = claim != null ? claim.ClaimValue : null
                                };

            // Pobieramy tylko 3 najnowsze opinie na stronê g³ówn¹
            var rawOpinions = await opinionsQuery.Take(3).AsNoTracking().ToListAsync();

            // Mapowanie danych (konwersja na ParsedOpinion)
            var parsedOpinions = rawOpinions.Select(x =>
            {
                int stars = 5;
                if (x.Subject.StartsWith("Ocena:"))
                {
                    int.TryParse(x.Subject.Replace("Ocena:", "").Trim(), out stars);
                }

                string displayName = "Goœæ";
                string initials = "G";

                if (!string.IsNullOrWhiteSpace(x.FullName))
                {
                    displayName = x.FullName;
                    var parts = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    initials = parts.Length >= 2
                        ? $"{parts[0][0]}{parts[1][0]}".ToUpper()
                        : displayName.Substring(0, 1).ToUpper();
                }
                else if (!string.IsNullOrWhiteSpace(x.Email))
                {
                    var emailParts = x.Email.Split('@');
                    if (emailParts.Length > 0)
                    {
                        displayName = emailParts[0];
                        initials = displayName.Substring(0, 1).ToUpper();
                    }
                }

                return new ParsedOpinion
                {
                    Id = x.Id,
                    UserId = x.UserId,
                    UserName = displayName,
                    Message = x.Message,
                    CreatedAt = x.CreatedAt,
                    Stars = stars,
                    Initials = initials
                };
            }).ToList();

            // 3. BUDOWANIE MODELU WIDOKU
            var vm = new HomeViewModel
            {
                FeaturedProducts = products,
                LatestOpinions = parsedOpinions
            };

            return View(vm);
        }

        public IActionResult Privacy() => View();
    }
}