
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MonitoringConfigurator.Areas.Identity.Pages.Account.Manage
{
    public partial class EmailModel : PageModel
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly SignInManager<IdentityUser> _signInManager;

        public EmailModel(UserManager<IdentityUser> userManager, SignInManager<IdentityUser> signInManager)
        {
            _userManager = userManager;
            _signInManager = signInManager;
        }

        [TempData]
        public string? StatusMessage { get; set; }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public string Email { get; set; } = "";

        public class InputModel
        {
            [Required]
            [EmailAddress]
            [Display(Name = "Nowy e‑mail")]
            public string? NewEmail { get; set; }
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound("Nie można załadować użytkownika.");

            Email = await _userManager.GetEmailAsync(user) ?? "";
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound("Nie można załadować użytkownika.");

            if (!ModelState.IsValid)
            {
                Email = await _userManager.GetEmailAsync(user) ?? "";
                return Page();
            }

            var result = await _userManager.SetEmailAsync(user, Input.NewEmail!);
            if (result.Succeeded)
            {
                await _userManager.SetUserNameAsync(user, Input.NewEmail!);
                await _signInManager.RefreshSignInAsync(user);
                StatusMessage = "E‑mail został zaktualizowany.";
                return RedirectToPage();
            }

            foreach (var e in result.Errors) ModelState.AddModelError(string.Empty, e.Description);
            Email = await _userManager.GetEmailAsync(user) ?? "";
            return Page();
        }
    }
}
