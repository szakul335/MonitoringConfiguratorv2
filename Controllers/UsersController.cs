
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MonitoringConfigurator.Controllers
{
    [Authorize(Roles = "Admin")]
    public class UsersController : Controller
    {
        private readonly UserManager<IdentityUser> _users;
        private readonly RoleManager<IdentityRole> _roles;

        public UsersController(UserManager<IdentityUser> users, RoleManager<IdentityRole> roles)
        {
            _users = users;
            _roles = roles;
        }

        public async Task<IActionResult> Index(string? search = null)
        {
            var q = _users.Users.AsQueryable();
            if (!string.IsNullOrWhiteSpace(search))
            {
                q = q.Where(u => u.Email!.Contains(search) || u.UserName!.Contains(search));
            }
            var list = await q.OrderBy(u => u.Email!).Take(1000).ToListAsync();
            var adminRole = "Admin";
            var operatorRole = "Operator";
            var vm = new List<UserVm>();
            foreach (var u in list)
            {
                var roles = await _users.GetRolesAsync(u);
                vm.Add(new UserVm
                {
                    Id = u.Id,
                    Email = u.Email ?? u.UserName ?? "",
                    IsAdmin = roles.Contains(adminRole),
                    IsOperator = roles.Contains(operatorRole)
                });
            }
            ViewBag.Search = search;
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MakeAdmin(string id)
        {
            var u = await _users.FindByIdAsync(id);
            if (u == null) return NotFound();
            await _users.AddToRoleAsync(u, "Admin");
            TempData["Toast"] = "Nadano rolę administratora.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveAdmin(string id)
        {
            var u = await _users.FindByIdAsync(id);
            if (u == null) return NotFound();
            await _users.RemoveFromRoleAsync(u, "Admin");
            TempData["Toast"] = "Usunięto rolę administratora.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MakeOperator(string id)
        {
            var u = await _users.FindByIdAsync(id);
            if (u == null) return NotFound();
            await _users.AddToRoleAsync(u, "Operator");
            TempData["Toast"] = "Nadano rolę operatora.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveOperator(string id)
        {
            var u = await _users.FindByIdAsync(id);
            if (u == null) return NotFound();
            await _users.RemoveFromRoleAsync(u, "Operator");
            TempData["Toast"] = "Usunięto rolę operatora.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(string id)
        {
            var u = await _users.FindByIdAsync(id);
            if (u == null) return NotFound();
          
            if (User.Identity?.Name == u.UserName || User.Identity?.Name == u.Email)
            {
                TempData["Toast"] = "Nie możesz usunąć bieżącego konta.";
                return RedirectToAction(nameof(Index));
            }
            await _users.DeleteAsync(u);
            TempData["Toast"] = "Użytkownik usunięty.";
            return RedirectToAction(nameof(Index));
        }

        public class UserVm
        {
            public string Id { get; set; } = "";
            public string Email { get; set; } = "";
            public bool IsAdmin { get; set; }
            public bool IsOperator { get; set; }
        }
    }
}
