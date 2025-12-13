using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MonitoringConfigurator.Data;
using MonitoringConfigurator.Models;
using System.Text.Json;

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

        // Lista zamówień użytkownika
        public async Task<IActionResult> Index()
        {
            var userId = _userManager.GetUserId(User);
            var orders = await _ctx.Orders
                .Where(o => o.UserId == userId)
                .OrderByDescending(o => o.OrderDate)
                .Include(o => o.Items)
                .ToListAsync();

            return View(orders);
        }

        // Szczegóły zamówienia
        public async Task<IActionResult> Details(int id)
        {
            var userId = _userManager.GetUserId(User);
            var order = await _ctx.Orders
                .Include(o => o.Items)
                .ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId);

            if (order == null) return NotFound();

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

            // Uwaga: Usługa montażu nie jest produktem w bazie, więc na razie ją pomijamy w strukturze relacyjnej,
            // chyba że dodasz "Produkt-Usługa" do bazy. W tym modelu zapisujemy tylko fizyczne towary.

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