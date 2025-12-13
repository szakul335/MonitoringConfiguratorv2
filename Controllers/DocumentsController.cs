using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MonitoringConfigurator.Data;
using System.IO;
using System.Threading.Tasks;

namespace MonitoringConfigurator.Controllers
{
    [Authorize]
    public class DocumentsController : Controller
    {
        private readonly AppDbContext _ctx;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IWebHostEnvironment _env;

        public DocumentsController(AppDbContext ctx, UserManager<IdentityUser> userManager, IWebHostEnvironment env)
        {
            _ctx = ctx;
            _userManager = userManager;
            _env = env;
        }

        // Wyświetlanie listy dokumentów z bazy danych
        public async Task<IActionResult> Index()
        {
            var userId = _userManager.GetUserId(User);

            var docs = await _ctx.UserDocuments
                .Where(d => d.UserId == userId)
                .OrderByDescending(d => d.CreatedUtc)
                .ToListAsync();

            return View(docs);
        }

        // Pobieranie pliku (PDF)
        public async Task<IActionResult> Download(int id)
        {
            var userId = _userManager.GetUserId(User);
            var doc = await _ctx.UserDocuments.FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId);

            if (doc == null) return NotFound();

            return File(doc.Content, "application/pdf", doc.Title + ".pdf");
        }

        // Usuwanie dokumentu
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var userId = _userManager.GetUserId(User);
            var doc = await _ctx.UserDocuments.FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId);

            if (doc == null) return NotFound();

            _ctx.UserDocuments.Remove(doc);
            await _ctx.SaveChangesAsync();

            TempData["Message"] = "Dokument został usunięty.";
            return RedirectToAction(nameof(Index));
        }
    }
}