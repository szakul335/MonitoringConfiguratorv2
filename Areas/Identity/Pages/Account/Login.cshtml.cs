using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MonitoringConfigurator.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class LoginModel : PageModel
    {
        private readonly SignInManager<IdentityUser> _signInManager;

        public LoginModel(SignInManager<IdentityUser> signInManager)
        {
            _signInManager = signInManager;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public string ReturnUrl { get; set; } = "/";

        public class InputModel
        {
            [Required(ErrorMessage = "E-mail jest wymagany.")]
            [EmailAddress(ErrorMessage = "Nieprawidłowy format e-mail.")]
            public string Email { get; set; } = string.Empty;

            [Required(ErrorMessage = "Hasło jest wymagane.")]
            [DataType(DataType.Password)]
            public string Password { get; set; } = string.Empty;

            [Display(Name = "Zapamiętaj mnie")]
            public bool RememberMe { get; set; }
        }

        public void OnGet(string? returnUrl = null)
        {
            ReturnUrl = returnUrl ?? "/";
        }

        public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
        {
            ReturnUrl = returnUrl ?? "/";

            if (!ModelState.IsValid) return Page();

            var result = await _signInManager.PasswordSignInAsync(Input.Email, Input.Password, Input.RememberMe, lockoutOnFailure: true);
            if (result.Succeeded) return LocalRedirect(ReturnUrl);
            if (result.IsLockedOut)
            {
                ModelState.AddModelError(string.Empty, "Konto zostało tymczasowo zablokowane.");
                return Page();
            }

            ModelState.AddModelError(string.Empty, "Nieprawidłowy e-mail lub hasło.");
            return Page();
        }
    }
}
