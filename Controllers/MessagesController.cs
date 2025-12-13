using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MonitoringConfigurator.Data;
using MonitoringConfigurator.Models;
using System.Linq;
using System.Threading.Tasks;

namespace MonitoringConfigurator.Controllers
{
    [Authorize(Roles = "Admin,Operator")]
    public class MessagesController : Controller
    {
        private readonly AppDbContext _ctx;
        private readonly UserManager<IdentityUser> _userManager;

        public MessagesController(AppDbContext ctx, UserManager<IdentityUser> userManager)
        {
            _ctx = ctx;
            _userManager = userManager;
        }

       
        public async Task<IActionResult> Index()
        {
            var list = await _ctx.Contacts
                .OrderByDescending(c => c.CreatedAt)
                .Take(1000)
                .ToListAsync();
            return View(list);
        }

       
        public async Task<IActionResult> Details(int id)
        {
            var msg = await _ctx.Contacts.FirstOrDefaultAsync(c => c.Id == id);
            if (msg == null) return NotFound();
            return View(msg);
        }

       
        public async Task<IActionResult> Delete(int id)
        {
            var msg = await _ctx.Contacts.FirstOrDefaultAsync(c => c.Id == id);
            if (msg == null) return NotFound();
            return View(msg);
        }

    
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var msg = await _ctx.Contacts.FirstOrDefaultAsync(c => c.Id == id);
            if (msg == null) return NotFound();
            _ctx.Contacts.Remove(msg);
            await _ctx.SaveChangesAsync();
            TempData["Msg"] = "Wiadomość usunięta.";
            return RedirectToAction(nameof(Index));
        }

      
        [Authorize]
        public async Task<IActionResult> Mine()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            var myMsgs = await _ctx.Contacts
                .Where(c => c.UserId == user.Id)
                .OrderByDescending(c => c.CreatedAt)
                .Take(200)
                .ToListAsync();
            return View(myMsgs);
        }
    }
}
