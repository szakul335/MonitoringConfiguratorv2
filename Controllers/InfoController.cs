using Microsoft.AspNetCore.Authorization;
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
    public class InfoController : Controller
    {
        private readonly AppDbContext _ctx;
        private readonly UserManager<IdentityUser> _userManager;

        public InfoController(AppDbContext ctx, UserManager<IdentityUser> userManager)
        {
            _ctx = ctx;
            _userManager = userManager;
        }

        // --- ZAKŁADKI INFORMACYJNE (Przywrócone) ---

        [HttpGet, AllowAnonymous]
        public IActionResult Contact() => View();

        [HttpGet, AllowAnonymous]
        public IActionResult Faq() => View();

        [HttpGet, AllowAnonymous]
        public IActionResult About() => View();

        [HttpGet, AllowAnonymous]
        public IActionResult Guides() => View();

        // --- OBSŁUGA OPINII (Nowa logika) ---

        [HttpGet, AllowAnonymous]
        public async Task<IActionResult> Opinions(string sort = "newest")
        {
            // 1. Zapytanie LINQ: Opinie + Użytkownik + Imię (Claim 'profile:fullName')
            var query = from c in _ctx.Contacts
                        where c.Subject.StartsWith("Ocena:") || c.Subject == "Opinia o aplikacji"

                        // Dołączamy użytkownika (LEFT JOIN)
                        join u in _ctx.Users on c.UserId equals u.Id into users
                        from user in users.DefaultIfEmpty()

                            // Dołączamy Claim z imieniem (LEFT JOIN)
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

            var rawData = await query.AsNoTracking().ToListAsync();

            // 2. Przekształcenie danych w pamięci
            var parsedList = rawData.Select(x =>
            {
                int stars = 5;
                if (x.Subject.StartsWith("Ocena:"))
                {
                    int.TryParse(x.Subject.Replace("Ocena:", "").Trim(), out stars);
                }

                // LOGIKA NAZWY: Imię > Część E-maila > Gość
                string displayName = "Gość";
                string initials = "G";

                if (!string.IsNullOrWhiteSpace(x.FullName))
                {
                    displayName = x.FullName;
                    var parts = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                        initials = $"{parts[0][0]}{parts[1][0]}".ToUpper();
                    else
                        initials = displayName.Substring(0, 1).ToUpper();
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

            // 3. Obliczenie statystyk
            var vm = new OpinionViewModel
            {
                TotalCount = parsedList.Count,
                CurrentSort = sort
            };

            if (vm.TotalCount > 0)
            {
                vm.AverageRating = parsedList.Average(x => x.Stars);
                foreach (var item in parsedList)
                {
                    if (item.Stars >= 1 && item.Stars <= 5)
                        vm.StarCounts[item.Stars]++;
                }
            }

            // 4. Sortowanie listy
            vm.Reviews = sort switch
            {
                "highest" => parsedList.OrderByDescending(x => x.Stars).ThenByDescending(x => x.CreatedAt).ToList(),
                "lowest" => parsedList.OrderBy(x => x.Stars).ThenByDescending(x => x.CreatedAt).ToList(),
                _ => parsedList.OrderByDescending(x => x.CreatedAt).ToList()
            };

            return View(vm);
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Opinions(OpinionViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return await Opinions("newest");
            }

            var user = await _userManager.GetUserAsync(User);

            var opinion = new Contact
            {
                UserId = user?.Id,
                Subject = $"Ocena: {model.Rating}",
                Message = model.Message,
                CreatedAt = DateTime.UtcNow
            };

            _ctx.Contacts.Add(opinion);
            await _ctx.SaveChangesAsync();

            TempData["Msg"] = "Twoja opinia została dodana!";
            return RedirectToAction(nameof(Opinions));
        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteOpinion(int id)
        {
            var opinion = await _ctx.Contacts
                .Where(c => (c.Subject == "Opinia o aplikacji" || c.Subject.StartsWith("Ocena:")) && c.Id == id)
                .FirstOrDefaultAsync();

            if (opinion == null) return NotFound();

            _ctx.Contacts.Remove(opinion);
            await _ctx.SaveChangesAsync();

            TempData["Msg"] = "Opinia została usunięta.";
            return RedirectToAction(nameof(Opinions));
        }

        // --- WSPARCIE I PROFIL (Przywrócone) ---

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> Support()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var sent = await _ctx.Contacts
                .Where(c => c.UserId == user.Id)
                .OrderByDescending(c => c.CreatedAt)
                .Take(200)
                .ToListAsync();

            var vm = new SupportViewModel { Recipient = "Admin", Sent = sent };
            return View(vm);
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Support(SupportViewModel model)
        {
            var user = await _userManager.GetUserAsync(User);
            if (!ModelState.IsValid)
            {
                if (user == null) return Challenge();
                model.Sent = await _ctx.Contacts
                    .Where(c => c.UserId == user.Id)
                    .OrderByDescending(c => c.CreatedAt)
                    .Take(200)
                    .ToListAsync();
                return View(model);
            }

            if (user == null) return Challenge();

            var prefix = model.Recipient == "Operator" ? "[Operator] " : "[Admin] ";
            var contact = new Contact
            {
                UserId = user.Id,
                Subject = prefix + model.Subject,
                Message = model.Message
            };
            _ctx.Contacts.Add(contact);
            await _ctx.SaveChangesAsync();

            ViewBag.Success = "Wiadomość została wysłana.";

            var sent = await _ctx.Contacts
                .Where(c => c.UserId == user.Id)
                .OrderByDescending(c => c.CreatedAt)
                .Take(200)
                .ToListAsync();

            var vm = new SupportViewModel { Recipient = model.Recipient, Sent = sent };
            ModelState.Clear();
            return View(vm);
        }

        [Authorize]
        [HttpGet]
        public IActionResult Profile() => View();
    }
}