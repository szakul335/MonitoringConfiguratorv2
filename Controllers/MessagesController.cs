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

        // INDEX
        public async Task<IActionResult> Index(int page = 1, ContactStatus? status = null, ContactType? type = null)
        {
            int pageSize = 10;
            var query = _ctx.Contacts
                .Include(c => c.Replies)
                .Where(c => c.ParentId == null);

            if (status.HasValue) query = query.Where(c => c.Status == status.Value);
            if (type.HasValue) query = query.Where(c => c.Type == type.Value);

            int totalItems = await query.CountAsync();
            var list = await query
                .OrderByDescending(c => c.Status == ContactStatus.New)
                .ThenByDescending(c => c.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var userIds = list.Where(c => c.UserId != null).Select(c => c.UserId).Distinct().ToList();
            var users = await _ctx.Users
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Email);
            ViewBag.UserEmails = users;

            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
            ViewBag.CurrentStatus = status;
            ViewBag.CurrentType = type;

            return View(list);
        }

        // DETAILS
        // DETAILS
        public async Task<IActionResult> Details(int id)
        {
            var thread = await _ctx.Contacts
                .Include(c => c.Replies)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (thread == null) return NotFound();

            bool statusChanged = false;

            // Logika automatycznego zamykania Opinii
            if (thread.Type == ContactType.Opinion && thread.Status != ContactStatus.Closed)
            {
                thread.Status = ContactStatus.Closed;
                statusChanged = true;
            }
            // Logika zmiany statusu z Nowe na W trakcie (tylko dla Support/General)
            else if (thread.Type != ContactType.Opinion && thread.Status == ContactStatus.New)
            {
                thread.Status = ContactStatus.Read;
                statusChanged = true;
            }

            if (statusChanged)
            {
                await _ctx.SaveChangesAsync();
            }

            ViewBag.UserEmail = "Gość";
            if (thread.UserId != null)
            {
                var user = await _userManager.FindByIdAsync(thread.UserId);
                if (user != null) ViewBag.UserEmail = user.Email;
            }
            else if (!string.IsNullOrEmpty(thread.GuestEmail))
            {
                ViewBag.UserEmail = thread.GuestEmail + " (Gość)";
            }

            return View(thread);
        }

        // Akcja dla przycisków w nagłówku (Otwórz/Zamknij)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangeStatus(int id, ContactStatus newStatus)
        {
            var thread = await _ctx.Contacts.FindAsync(id);
            if (thread == null) return NotFound();

            thread.Status = newStatus;
            await _ctx.SaveChangesAsync();

            TempData["Msg"] = $"Status zgłoszenia zmieniony na: {newStatus}.";
            return RedirectToAction(nameof(Details), new { id = id });
        }

        // Akcja dla formularza odpowiedzi
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdminReply(int id, string message, bool closeTicket)
        {
            var user = await _userManager.GetUserAsync(User);
            var thread = await _ctx.Contacts.FindAsync(id);

            if (thread == null) return NotFound();

            if (!string.IsNullOrWhiteSpace(message))
            {
                var reply = new Contact
                {
                    UserId = user.Id,
                    Type = thread.Type,
                    Status = ContactStatus.Replied,
                    Subject = "RE: " + thread.Subject,
                    Message = message,
                    CreatedAt = DateTime.UtcNow,
                    ParentId = id
                };
                _ctx.Contacts.Add(reply);
            }

            // Aktualizacja statusu
            if (closeTicket)
                thread.Status = ContactStatus.Closed;
            else
                thread.Status = ContactStatus.Replied;

            await _ctx.SaveChangesAsync();
            return RedirectToAction(nameof(Details), new { id = id });
        }
        // DELETE
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
            var thread = await _ctx.Contacts
                .Include(c => c.Replies)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (thread == null) return NotFound();

            if (thread.Replies != null && thread.Replies.Any())
                _ctx.Contacts.RemoveRange(thread.Replies);
            
            _ctx.Contacts.Remove(thread);
            await _ctx.SaveChangesAsync();
            
            TempData["Msg"] = "Wątek został usunięty.";
            return RedirectToAction(nameof(Index));
        }
    }
}