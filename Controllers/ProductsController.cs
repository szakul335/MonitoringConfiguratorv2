using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MonitoringConfigurator.Data;
using MonitoringConfigurator.Models;
using Microsoft.AspNetCore.Hosting; // Wymagane
using Microsoft.AspNetCore.Http;    // Wymagane
using System.IO;                    // Wymagane
using System.Linq;
using System.Threading.Tasks;

namespace MonitoringConfigurator.Controllers
{
    public class ProductsController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env; // Dodano pole środowiska

        public ProductsController(AppDbContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env; // Wstrzykiwanie środowiska
        }

        // --- STRONA KATALOGU (Dla wszystkich) ---
        public async Task<IActionResult> Index(ProductCatalogViewModel vm)
        {
            var query = _context.Products.AsNoTracking().AsQueryable();

            if (vm.Category.HasValue)
            {
                query = query.Where(p => p.Category == vm.Category.Value);
            }

            if (!string.IsNullOrWhiteSpace(vm.Query))
            {
                var q = vm.Query.Trim();
                query = query.Where(p =>
                    p.Name.Contains(q) ||
                    (p.Brand != null && p.Brand.Contains(q)) ||
                    (p.Model != null && p.Model.Contains(q)));
            }

            if (vm.MinPrice.HasValue)
            {
                query = query.Where(p => p.Price >= vm.MinPrice.Value);
            }

            if (vm.MaxPrice.HasValue)
            {
                query = query.Where(p => p.Price <= vm.MaxPrice.Value);
            }

            if (vm.MinResolution.HasValue)
            {
                query = query.Where(p => p.ResolutionMp >= vm.MinResolution.Value);
            }

            if (vm.OutdoorOnly)
            {
                query = query.Where(p => p.Outdoor == true);
            }

            query = vm.SortBy switch
            {
                "price_asc" => query.OrderBy(p => p.Price),
                "price_desc" => query.OrderByDescending(p => p.Price),
                "name_desc" => query.OrderByDescending(p => p.Name),
                _ => query.OrderBy(p => p.Name)
            };

            vm.Products = await query.ToListAsync();

            return View(vm);
        }

        public async Task<IActionResult> Details(int id)
        {
            var product = await _context.Products
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == id);

            if (product == null)
            {
                return NotFound();
            }

            return View(product);
        }

        // --- PANEL ADMINISTRATORA (Wymaga roli Admin) ---

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Manage(int? id, ProductCategory? category, string? query)
        {
            var products = await BuildFilteredQuery(category, query)
                .AsNoTracking()
                .OrderBy(p => p.Name)
                .ToListAsync();

            var editableProduct = id.HasValue
                ? await _context.Products.FindAsync(id.Value)
                : new Product();

            if (id.HasValue && editableProduct == null)
            {
                return NotFound();
            }

            var vm = new ProductManagementViewModel
            {
                Category = category,
                Query = query,
                Products = products,
                EditableProduct = editableProduct
            };

            return View(vm);
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Manage(ProductManagementViewModel viewModel)
        {
            // --- LOGIKA OBSŁUGI ZDJĘĆ ---
            var file = viewModel.EditableProduct.ImageUpload;
            if (file != null && file.Length > 0)
            {
                var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (ext != ".jpg" && ext != ".jpeg" && ext != ".png")
                {
                    ModelState.AddModelError("EditableProduct.ImageUpload", "Dozwolone są tylko pliki .jpg i .png");
                }
                else
                {
                    // Upewnij się, że folder istnieje
                    var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "products");
                    if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

                    // Unikalna nazwa pliku
                    var uniqueFileName = System.Guid.NewGuid().ToString() + ext;
                    var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                    // Zapis na dysk
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await file.CopyToAsync(stream);
                    }

                    // Przypisanie ścieżki do modelu (zastępuje stary URL)
                    viewModel.EditableProduct.ImageUrl = "/uploads/products/" + uniqueFileName;
                }
            }
            // ---------------------------

            if (!ModelState.IsValid)
            {
                viewModel.Products = await BuildFilteredQuery(viewModel.Category, viewModel.Query)
                    .AsNoTracking()
                    .OrderBy(p => p.Name)
                    .ToListAsync();

                return View(viewModel);
            }

            var isEdit = viewModel.EditableProduct.Id != 0;

            if (isEdit)
            {
                var exists = await _context.Products.AnyAsync(p => p.Id == viewModel.EditableProduct.Id);
                if (!exists) return NotFound();

                _context.Entry(viewModel.EditableProduct).State = EntityState.Modified;
                TempData["Toast"] = "Zmiany zostały zapisane.";
            }
            else
            {
                _context.Products.Add(viewModel.EditableProduct);
                TempData["Toast"] = "Produkt został dodany.";
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Manage), new { category = viewModel.Category, query = viewModel.Query });
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var product = await _context.Products.FindAsync(id);
            if (product == null) return NotFound();

            // Opcjonalnie: Usunięcie pliku z dysku przy usuwaniu produktu
            if (!string.IsNullOrEmpty(product.ImageUrl) && product.ImageUrl.StartsWith("/uploads/products/"))
            {
                var path = Path.Combine(_env.WebRootPath, product.ImageUrl.TrimStart('/'));
                if (System.IO.File.Exists(path))
                {
                    System.IO.File.Delete(path);
                }
            }

            _context.Products.Remove(product);
            await _context.SaveChangesAsync();
            TempData["Toast"] = "Produkt został usunięty.";

            return RedirectToAction(nameof(Manage));
        }

        // --- Metody pomocnicze ---
        private IQueryable<Product> BuildFilteredQuery(ProductCategory? category, string? query)
        {
            var products = _context.Products.AsQueryable();

            if (category.HasValue)
                products = products.Where(p => p.Category == category.Value);

            if (!string.IsNullOrWhiteSpace(query))
            {
                query = query.Trim();
                products = products.Where(p =>
                    (p.Name != null && p.Name.Contains(query)) ||
                    (p.Brand != null && p.Brand.Contains(query)) ||
                    (p.Model != null && p.Model.Contains(query)));
            }
            return products;
        }
    }
}