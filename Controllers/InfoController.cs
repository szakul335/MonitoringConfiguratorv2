using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MonitoringConfigurator.Data;
using MonitoringConfigurator.Models;
using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace MonitoringConfigurator.Controllers
{
    // Model pomocniczy dla formularza kontaktowego
    public class ContactFormModel
    {
        [Required(ErrorMessage = "Imię jest wymagane")]
        public string Name { get; set; }

        [Required(ErrorMessage = "Email jest wymagany")]
        [EmailAddress(ErrorMessage = "Niepoprawny format adresu email")]
        public string Email { get; set; }

        [Required(ErrorMessage = "Temat jest wymagany")]
        public string Subject { get; set; }

        [Required(ErrorMessage = "Wiadomość jest wymagana")]
        public string Message { get; set; }
    }

    public class InfoController : Controller
    {
        private readonly AppDbContext _ctx;
        private readonly UserManager<IdentityUser> _userManager;

        public InfoController(AppDbContext ctx, UserManager<IdentityUser> userManager)
        {
            _ctx = ctx;
            _userManager = userManager;
        }

        // --- ZAKŁADKI INFORMACYJNE ---

        [HttpGet, AllowAnonymous]
        public IActionResult Contact() => View();

        [HttpPost, AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendContact(ContactFormModel model)
        {
            if (!ModelState.IsValid)
                return View("Contact", model);

            var contact = new Contact
            {
                Type = ContactType.General,
                Status = ContactStatus.New,
                GuestName = model.Name,
                GuestEmail = model.Email,
                Subject = model.Subject,
                Message = model.Message,
                CreatedAt = DateTime.UtcNow
                // UserId zostaje null
            };

            _ctx.Contacts.Add(contact);
            await _ctx.SaveChangesAsync();

            TempData["Success"] = "Wiadomość została wysłana. Dziękujemy!";
            return RedirectToAction(nameof(Contact));
        }

        [HttpGet, AllowAnonymous]
        public IActionResult Faq() => View();

        [HttpGet, AllowAnonymous]
        public IActionResult About() => View();

        [HttpGet, AllowAnonymous]
        public IActionResult Guides() => View();

        // --- OBSŁUGA OPINII ---

        [HttpGet, AllowAnonymous]
        public async Task<IActionResult> Opinions(string sort = "newest")
        {
        
            var query = from c in _ctx.Contacts
                        where c.Subject.StartsWith("Ocena:") || c.Subject == "Opinia o aplikacji"

            
                        join u in _ctx.Users on c.UserId equals u.Id into users
                        from user in users.DefaultIfEmpty()

                            // Dołączamy Claim z imieniem (LEFT JOIN)
                        join cl in _ctx.Set<IdentityUserClaim<string>>()
                            on new { UserId = user.Id, ClaimType = "profile:fullName" }
                            equals new { UserId = cl.UserId, ClaimType = cl.ClaimType } into claims
                        from claim in claims.DefaultIfEmpty()

                            // [NOWOŚĆ] Dołączamy Claim z awatarem (LEFT JOIN)
                        join av in _ctx.Set<IdentityUserClaim<string>>()
                            on new { UserId = user.Id, ClaimType = "profile:avatar" }
                            equals new { UserId = av.UserId, ClaimType = av.ClaimType } into avatars
                        from avatar in avatars.DefaultIfEmpty()

                        orderby c.CreatedAt descending
                        select new
                        {
                            c.Id,
                            c.UserId,
                            c.Subject,
                            c.Message,
                            c.CreatedAt,
                            Email = user != null ? user.Email : null,
                            FullName = claim != null ? claim.ClaimValue : null,
                            AvatarUrl = avatar != null ? avatar.ClaimValue : null // Pobieramy URL
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
                    Initials = initials,
                    AvatarUrl = x.AvatarUrl // Przypisujemy do modelu
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
                Type = ContactType.Opinion, // Ustawiamy typ na Opinię
                Status = ContactStatus.New,
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

        // --- WSPARCIE I PROFIL ---

        // --- WSPARCIE (Ticket System) ---

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> Support()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            // Pobieramy tylko GŁÓWNE wątki (ParentId == null)
            var threads = await _ctx.Contacts
                .Include(c => c.Replies) // Dołączamy odpowiedzi, aby wiedzieć ile ich jest
                .Where(c => c.UserId == user.Id && c.ParentId == null && c.Type == ContactType.Support)
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();

            // Używamy tego samego modelu co wcześniej, ale w liście 'Sent' są teraz wątki
            var vm = new SupportViewModel { Recipient = "Admin", Sent = threads };
            return View(vm);
        }

        // TWORZENIE NOWEGO WĄTKU
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Support(SupportViewModel model)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            if (!ModelState.IsValid)
            {
                // W razie błędu ponownie ładujemy listę wątków
                model.Sent = await _ctx.Contacts
                    .Include(c => c.Replies)
                    .Where(c => c.UserId == user.Id && c.ParentId == null && c.Type == ContactType.Support)
                    .OrderByDescending(c => c.CreatedAt)
                    .ToListAsync();
                return View(model);
            }

            var prefix = model.Recipient == "Operator" ? "[Operator] " : "[Admin] ";
            var contact = new Contact
            {
                UserId = user.Id,
                Type = ContactType.Support,
                Status = ContactStatus.New,
                Subject = prefix + model.Subject,
                Message = model.Message,
                CreatedAt = DateTime.UtcNow,
                ParentId = null // To jest początek wątku
            };

            _ctx.Contacts.Add(contact);
            await _ctx.SaveChangesAsync();

            TempData["Success"] = "Nowe zgłoszenie zostało utworzone.";
            return RedirectToAction(nameof(Support));
        }

        // SZCZEGÓŁY WĄTKU + CZAT (Nowa Akcja)
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> TicketDetails(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            // Pobierz wątek wraz z odpowiedziami
            var thread = await _ctx.Contacts
                .Include(c => c.Replies)
                .FirstOrDefaultAsync(c => c.Id == id && c.UserId == user.Id);

            if (thread == null) return NotFound();

            return View(thread);
        }

        // ODPOWIEDŹ KLIENTA W ISTNIEJĄCYM WĄTKU
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReplyToTicket(int parentId, string message)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            if (string.IsNullOrWhiteSpace(message))
            {
                return RedirectToAction(nameof(TicketDetails), new { id = parentId });
            }

            var parentThread = await _ctx.Contacts.FirstOrDefaultAsync(c => c.Id == parentId && c.UserId == user.Id);
            if (parentThread == null) return NotFound();

            // Zmieniamy status głównego wątku na 'New' (bo klient odpisał, więc admin musi zobaczyć)
            parentThread.Status = ContactStatus.New;

            var reply = new Contact
            {
                UserId = user.Id, // Klient odpisuje
                Type = ContactType.Support,
                Status = ContactStatus.New,
                Subject = "RE: " + parentThread.Subject,
                Message = message,
                CreatedAt = DateTime.UtcNow,
                ParentId = parentId // Podpinamy pod wątek
            };

            _ctx.Contacts.Add(reply);
            await _ctx.SaveChangesAsync();

            return RedirectToAction(nameof(TicketDetails), new { id = parentId });
        }

        [Authorize]
        [HttpGet]
        public IActionResult Profile() => View();
    }
}