using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MonitoringConfigurator.Data;
using MonitoringConfigurator.Models;
using System.Text.Json;
using System.Linq;
using System.Threading.Tasks;
using System;

namespace MonitoringConfigurator.Controllers
{
    [Authorize]
    public class OrdersController : Controller
    {
        private readonly AppDbContext _ctx;
        private readonly UserManager<IdentityUser> _userManager;

        public OrdersController(AppDbContext ctx, UserManager<IdentityUser> userManager)
        {
            _ctx = ctx;
            _userManager = userManager;
        }

        // Lista zamówień
        public async Task<IActionResult> Index()
        {
            var userId = _userManager.GetUserId(User);
            bool isStaff = User.IsInRole("Admin") || User.IsInRole("Operator");

            // Budujemy zapytanie podstawowe
            var query = _ctx.Orders
                .Include(o => o.Items)
                .AsQueryable();

            // Jeśli to zwykły użytkownik (nie Admin/Operator), filtrujemy tylko jego zamówienia
            if (!isStaff)
            {
                query = query.Where(o => o.UserId == userId);
            }

            var orders = await query
                .OrderByDescending(o => o.OrderDate)
                .ToListAsync();

            // Opcjonalnie: Przekazujemy listę emaili użytkowników do widoku (dla Admina),
            // aby wyświetlić email zamiast samego ID.
            if (isStaff)
            {
                var userIds = orders.Select(o => o.UserId).Distinct().ToList();
                var users = await _ctx.Users
                    .Where(u => userIds.Contains(u.Id))
                    .ToDictionaryAsync(u => u.Id, u => u.Email);
                ViewBag.UserEmails = users;
            }

            return View(orders);
        }

        // Szczegóły zamówienia
        public async Task<IActionResult> Details(int id)
        {
            var userId = _userManager.GetUserId(User);
            bool isStaff = User.IsInRole("Admin") || User.IsInRole("Operator");

            var query = _ctx.Orders
                .Include(o => o.Items)
                .ThenInclude(i => i.Product)
                .AsQueryable();

            // Zabezpieczenie: zwykły użytkownik widzi tylko swoje, Staff widzi wszystko
            if (!isStaff)
            {
                query = query.Where(o => o.UserId == userId);
            }

            var order = await query.FirstOrDefaultAsync(o => o.Id == id);

            if (order == null) return NotFound();

            // DLA ADMINA/OPERATORA: Pobieramy pełne dane właściciela zamówienia
            if (isStaff)
            {
                var owner = await _userManager.FindByIdAsync(order.UserId);
                if (owner != null)
                {
                    // Pobieramy dodatkowe dane (claims: adres, imię, firma)
                    var claims = await _userManager.GetClaimsAsync(owner);

                    // Przekazujemy do widoku
                    ViewBag.OwnerUser = owner;
                    ViewBag.OwnerClaims = claims.ToDictionary(c => c.Type, c => c.Value);
                }
            }

            return View(order);
        }

        // Akcja tworząca zamówienie na podstawie JSON z konfiguratora
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateFromConfig(string jsonResult)
        {
            if (string.IsNullOrEmpty(jsonResult)) return RedirectToAction("Index", "Configurator");

            ConfigurationResult? config;
            try { config = JsonSerializer.Deserialize<ConfigurationResult>(jsonResult); }
            catch { return BadRequest(); }

            if (config == null) return BadRequest();

            var userId = _userManager.GetUserId(User);
            if (userId == null) return Challenge();

            // Tworzenie zamówienia
            var order = new Order
            {
                UserId = userId,
                OrderDate = DateTime.UtcNow,
                TotalAmount = config.TotalPrice
            };

            // Pomocnicza funkcja dodawania pozycji
            void AddItem(Product? p, int qty)
            {
                if (p != null && qty > 0)
                {
                    order.Items.Add(new OrderDetail
                    {
                        ProductId = p.Id,
                        Quantity = qty,
                        UnitPrice = p.Price
                    });
                }
            }

            // Przepisanie produktów z konfiguracji do zamówienia
            AddItem(config.SelectedOutdoorCam, config.Input.OutdoorCamCount);
            AddItem(config.SelectedIndoorCam, config.Input.IndoorCamCount);
            AddItem(config.SelectedRecorder, config.RecorderQuantity);
            AddItem(config.SelectedDisk, config.DiskQuantity);
            AddItem(config.SelectedSwitch, config.SwitchQuantity);
            AddItem(config.SelectedCable, config.CableQuantity);
            AddItem(config.SelectedMount, config.MountQuantity);
            AddItem(config.SelectedMonitor, config.MonitorQuantity);
            AddItem(config.SelectedUps, config.UpsQuantity);

            _ctx.Orders.Add(order);
            await _ctx.SaveChangesAsync();

            return RedirectToAction(nameof(Success), new { id = order.Id });
        }

        public IActionResult Success(int id)
        {
            return View(id);
        }
    }
}