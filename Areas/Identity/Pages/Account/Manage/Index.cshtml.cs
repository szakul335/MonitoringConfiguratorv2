using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using System.IO;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MonitoringConfigurator.Areas.Identity.Pages.Account.Manage
{
    public partial class IndexModel : PageModel
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IWebHostEnvironment _env;
        private readonly SignInManager<IdentityUser> _signInManager;

        public IndexModel(
            UserManager<IdentityUser> userManager,
            SignInManager<IdentityUser> signInManager,
            IWebHostEnvironment env)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _env = env;
        }

        public class AddressModel
        {
            [Display(Name = "Ulica i nr")] public string? Street { get; set; }
            [Display(Name = "Kod pocztowy")] public string? PostalCode { get; set; }
            [Display(Name = "Miasto")] public string? City { get; set; }
            [Display(Name = "Kraj")] public string? Country { get; set; }
        }

        public class DeleteModel
        {
            [Display(Name = "Potwierdzenie")] public string? Confirm { get; set; }
        }

        public string Username { get; set; } = "";
        public string UserId { get; set; } = "";
        public string Initials { get; set; } = "U";
        public string Email { get; set; } = "";
        public bool HasPassword { get; set; }
        public string AvatarUrl { get; set; } = "";

        [TempData] public string? StatusMessage { get; set; }

        [BindProperty] public InputModel Input { get; set; } = new();
        [BindProperty] public PrefsModel Prefs { get; set; } = new();
        [BindProperty] public AddressModel Address { get; set; } = new();
        [BindProperty] public DeleteModel Delete { get; set; } = new();

        public class InputModel
        {
            [Display(Name = "Imię i nazwisko")]
            public string? FullName { get; set; }

            [Display(Name = "Firma")]
            public string? Company { get; set; }

            [Phone]
            [Display(Name = "Numer telefonu")]
            public string? PhoneNumber { get; set; }
        }

        public class PrefsModel
        {
            [Display(Name = "Język")]
            public string Language { get; set; } = "pl";

            [Display(Name = "Motyw")]
            public string Theme { get; set; } = "dark";

            [Display(Name = "Newsletter")]
            public bool Newsletter { get; set; } = true;
        }

        private async Task<string?> GetClaimAsync(IdentityUser user, string type)
        {
            var claims = await _userManager.GetClaimsAsync(user);
            return claims.FirstOrDefault(c => c.Type == type)?.Value;
        }

        private async Task SetClaimAsync(IdentityUser user, string type, string? value)
        {
            var claims = await _userManager.GetClaimsAsync(user);
            var existing = claims.FirstOrDefault(c => c.Type == type);
            if (existing != null)
                await _userManager.RemoveClaimAsync(user, existing);

            if (!string.IsNullOrWhiteSpace(value))
                await _userManager.AddClaimAsync(user, new Claim(type, value));
        }

        private static string GetInitials(string? text)
        {
            var source = (text ?? "").Trim();
            if (string.IsNullOrEmpty(source)) return "U";
            var name = source.Contains('@') ? source.Split('@')[0] : source;
            var parts = name.Split(new[] { ' ', '-', '_', '.' }, System.StringSplitOptions.RemoveEmptyEntries);
            var init = string.Join("", parts.Select(p => p[0])).ToUpperInvariant();
            return init.Length > 3 ? init.Substring(0, 3) : (init.Length == 0 ? "U" : init);
        }

        private async Task LoadAsync(IdentityUser user)
        {
            UserId = user.Id;
            Username = await _userManager.GetUserNameAsync(user) ?? "";
            Email = await _userManager.GetEmailAsync(user) ?? "";
            HasPassword = await _userManager.HasPasswordAsync(user);
            Initials = GetInitials(Email ?? Username);

            Input = new InputModel
            {
                PhoneNumber = await _userManager.GetPhoneNumberAsync(user),
                FullName = await GetClaimAsync(user, "profile:fullName"),
                Company = await GetClaimAsync(user, "profile:company")
            };

            Address = new AddressModel
            {
                Street = await GetClaimAsync(user, "addr:street"),
                PostalCode = await GetClaimAsync(user, "addr:postal"),
                City = await GetClaimAsync(user, "addr:city"),
                Country = await GetClaimAsync(user, "addr:country")
            };

            AvatarUrl = await GetClaimAsync(user, "profile:avatar") ?? "";

            Prefs = new PrefsModel
            {
                Language = await GetClaimAsync(user, "pref:language") ?? "pl",
                Theme = await GetClaimAsync(user, "pref:theme") ?? "dark",
                Newsletter = (await GetClaimAsync(user, "pref:newsletter")) == "true"
            };
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound("Nie można załadować użytkownika.");
            await LoadAsync(user);
            return Page();
        }

        public async Task<IActionResult> OnPostSaveProfileAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound("Nie można załadować użytkownika.");
            if (!ModelState.IsValid) { await LoadAsync(user); return Page(); }

            var phone = await _userManager.GetPhoneNumberAsync(user);
            if (Input.PhoneNumber != phone)
            {
                var setPhone = await _userManager.SetPhoneNumberAsync(user, Input.PhoneNumber);
                if (!setPhone.Succeeded)
                {
                    StatusMessage = "Błąd przy zapisie numeru telefonu.";
                    return RedirectToPage();
                }
            }

            await SetClaimAsync(user, "profile:fullName", Input.FullName);
            await SetClaimAsync(user, "profile:company", Input.Company);

            await _signInManager.RefreshSignInAsync(user);
            StatusMessage = "Profil został zaktualizowany.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostSavePrefsAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound("Nie można załadować użytkownika.");
            if (!ModelState.IsValid) { await LoadAsync(user); return Page(); }

            await SetClaimAsync(user, "pref:language", Prefs.Language);
            await SetClaimAsync(user, "pref:theme", Prefs.Theme);
            await SetClaimAsync(user, "pref:newsletter", Prefs.Newsletter ? "true" : "false");

            await _signInManager.RefreshSignInAsync(user);
            StatusMessage = "Preferencje zapisane.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostSaveAddressAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound("Nie można załadować użytkownika.");
            if (!ModelState.IsValid) { await LoadAsync(user); return Page(); }

            await SetClaimAsync(user, "addr:street", Address.Street);
            await SetClaimAsync(user, "addr:postal", Address.PostalCode);
            await SetClaimAsync(user, "addr:city", Address.City);
            await SetClaimAsync(user, "addr:country", Address.Country);

            await _signInManager.RefreshSignInAsync(user);
            StatusMessage = "Adres zapisany.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostUploadAvatarAsync(IFormFile avatar)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound("Nie można załadować użytkownika.");
            if (avatar == null || avatar.Length == 0)
            {
                StatusMessage = "Nie wybrano pliku.";
                return RedirectToPage();
            }
            if (avatar.Length > 2 * 1024 * 1024)
            {
                StatusMessage = "Plik jest zbyt duży (max 2MB).";
                return RedirectToPage();
            }

            var ext = Path.GetExtension(avatar.FileName).ToLowerInvariant();
            if (ext != ".jpg" && ext != ".jpeg" && ext != ".png") ext = ".jpg";

            var dir = Path.Combine(_env.WebRootPath ?? "wwwroot", "uploads", "avatars");
            Directory.CreateDirectory(dir);
            var fileName = user.Id + ext;
            var full = Path.Combine(dir, fileName);
            using (var fs = System.IO.File.Create(full)) { await avatar.CopyToAsync(fs); }

            var rel = "/uploads/avatars/" + fileName;
            await SetClaimAsync(user, "profile:avatar", rel);
            await _signInManager.RefreshSignInAsync(user);
            StatusMessage = "Avatar zaktualizowany.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostExportDataAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound("Nie można załadować użytkownika.");
            var claims = await _userManager.GetClaimsAsync(user);

            var payload = new
            {
                userId = user.Id,
                username = await _userManager.GetUserNameAsync(user),
                email = await _userManager.GetEmailAsync(user),
                phone = await _userManager.GetPhoneNumberAsync(user),
                claims = claims.ToDictionary(c => c.Type, c => c.Value)
            };
            var json = System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            return File(bytes, "application/json", "moje-dane.json");
        }

        public async Task<IActionResult> OnPostDeleteAccountAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound("Nie można załadować użytkownika.");
            if (!string.Equals(Delete.Confirm?.Trim(), "USUŃ", System.StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = "Aby usunąć konto, wpisz USUŃ.";
                await LoadAsync(user);
                return Page();
            }
            var result = await _userManager.DeleteAsync(user);
            if (!result.Succeeded)
            {
                StatusMessage = "Nie udało się usunąć konta.";
                return RedirectToPage();
            }
            await _signInManager.SignOutAsync();
            return RedirectToPage("/Account/Login", new { area = "Identity" });
        }
    }
}
