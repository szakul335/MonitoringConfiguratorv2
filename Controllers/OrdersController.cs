using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering; // Ważne: do obsługi SelectList
using Microsoft.EntityFrameworkCore;
using MonitoringConfigurator.Data;
using MonitoringConfigurator.Models;
using System.Text.Json;
using System.Linq;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;

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

            var query = _ctx.Orders
                .Include(o => o.Items) 
                .AsQueryable();

            if (!isStaff)
            {
                query = query.Where(o => o.UserId == userId);
            }

            var orders = await query
                .OrderByDescending(o => o.OrderDate)
                .ToListAsync();

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

        // Szczegóły
        public async Task<IActionResult> Details(int id)
        {
            var userId = _userManager.GetUserId(User);
            bool isStaff = User.IsInRole("Admin") || User.IsInRole("Operator");

            var query = _ctx.Orders
                .Include(o => o.Items) // Poprawione: Items
                .ThenInclude(i => i.Product)
                .AsQueryable();

            if (!isStaff)
            {
                query = query.Where(o => o.UserId == userId);
            }

            var order = await query.FirstOrDefaultAsync(o => o.Id == id);

            if (order == null) return NotFound();

            if (isStaff)
            {
                var owner = await _userManager.FindByIdAsync(order.UserId);
                if (owner != null)
                {
                    var claims = await _userManager.GetClaimsAsync(owner);
                    ViewBag.OwnerUser = owner;
                    ViewBag.OwnerClaims = claims.ToDictionary(c => c.Type, c => c.Value);
                }
            }

            return View(order);
        }

        // Zmiana statusu
        [HttpPost]
        [Authorize(Roles = "Admin,Operator")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangeStatus(int id, OrderStatus newStatus)
        {
            var order = await _ctx.Orders.FindAsync(id);
            if (order == null) return NotFound();

            order.Status = newStatus;
            await _ctx.SaveChangesAsync();

            TempData["Message"] = $"Status zamówienia #{id} został zmieniony na: {newStatus}.";
            return RedirectToAction(nameof(Details), new { id = id });
        }

        // Tworzenie z konfiguratora
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

            var order = new Order
            {
                UserId = userId,
                OrderDate = DateTime.UtcNow,
                TotalAmount = config.TotalPrice,
                Status = OrderStatus.Nowe
            };

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

        // -----------------------------------------------------------------------
        // SEKCJA ADMINISTRACYJNA (Poprawiona)
        // -----------------------------------------------------------------------

        // GET: Orders/Manage/5
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Manage(int? id)
        {
            if (id == null) return NotFound();

            var order = await _ctx.Orders
                .Include(o => o.Items) // Poprawione: Items
                .ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (order == null) return NotFound();

            var owner = await _userManager.FindByIdAsync(order.UserId);
            ViewBag.OwnerEmail = owner?.Email ?? "Nieznany";

            return View(order);
        }

        // GET: Orders/EditLineItem/5
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> EditLineItem(int? id)
        {
            if (id == null) return NotFound();

            // Używamy Set<OrderDetail> bo Items to kolekcja w Order
            var orderDetail = await _ctx.Set<OrderDetail>()
                .Include(od => od.Product)
                .FirstOrDefaultAsync(od => od.Id == id);

            if (orderDetail == null) return NotFound();

            ViewBag.ProductId = new SelectList(_ctx.Products, "Id", "Name", orderDetail.ProductId);
            return View(orderDetail);
        }

        // POST: Orders/EditLineItem
        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditLineItem(int id, int productId, int quantity)
        {
            var orderDetail = await _ctx.Set<OrderDetail>().FindAsync(id);
            if (orderDetail == null) return NotFound();

            orderDetail.ProductId = productId;
            orderDetail.Quantity = quantity;

        
            var product = await _ctx.Products.FindAsync(productId);
            if (product != null)
            {
                orderDetail.UnitPrice = product.Price; 
            }

            _ctx.Update(orderDetail);
            await _ctx.SaveChangesAsync();

            await RecalculateOrderTotal(orderDetail.OrderId);

            return RedirectToAction(nameof(Manage), new { id = orderDetail.OrderId });
        }

        // GET: Orders/AddLineItem/5
        [Authorize(Roles = "Admin")]
        public IActionResult AddLineItem(int id)
        {
            ViewBag.OrderId = id;
            ViewBag.ProductId = new SelectList(_ctx.Products, "Id", "Name");
            return View();
        }

        // POST: Orders/AddLineItem
        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddLineItem(int orderId, int productId, int quantity)
        {
            var product = await _ctx.Products.FindAsync(productId);
            if (product == null) return NotFound();

            var orderDetail = new OrderDetail
            {
                OrderId = orderId,
                ProductId = productId,
                Quantity = quantity,
                UnitPrice = product.Price // Poprawione: UnitPrice
            };

            _ctx.Set<OrderDetail>().Add(orderDetail);
            await _ctx.SaveChangesAsync();

            await RecalculateOrderTotal(orderId);

            return RedirectToAction(nameof(Manage), new { id = orderId });
        }

        // POST: Orders/DeleteLineItem/5
        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteLineItem(int id)
        {
            var orderDetail = await _ctx.Set<OrderDetail>().FindAsync(id);
            if (orderDetail != null)
            {
                int orderId = orderDetail.OrderId;
                _ctx.Set<OrderDetail>().Remove(orderDetail);
                await _ctx.SaveChangesAsync();

                await RecalculateOrderTotal(orderId);

                return RedirectToAction(nameof(Manage), new { id = orderId });
            }
            return NotFound();
        }

        private async Task RecalculateOrderTotal(int orderId)
        {
            var order = await _ctx.Orders
                .Include(o => o.Items) 
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order != null)
            {
              
                order.TotalAmount = order.Items.Sum(i => i.Quantity * i.UnitPrice);
                _ctx.Update(order);
                await _ctx.SaveChangesAsync();
            }
        }
    }
}