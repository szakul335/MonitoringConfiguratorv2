using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;

namespace MonitoringConfigurator.Controllers
{
    [Authorize]
    public class DocumentsController : Controller
    {
        private readonly IWebHostEnvironment _env;
        public DocumentsController(IWebHostEnvironment env) => _env = env;

        private string GetUserFolder()
        {
            var basePath = Path.Combine(_env.WebRootPath, "documents");
            var name = User?.Identity?.Name ?? "anonymous";
            var safe = Regex.Replace(name, @"[^a-zA-Z0-9_.@-]", "_");
            var userPath = Path.Combine(basePath, safe);
            Directory.CreateDirectory(userPath);
            return userPath;
        }

        public IActionResult Index(string? msg = null, string? err = null)
        {
            var folder = GetUserFolder();
            var files = Directory.GetFiles(folder)
                .Select(p => new DocItem
                {
                    Name = Path.GetFileName(p),
                    SizeBytes = new FileInfo(p).Length,
                    Url = Url.Content($"~/documents/{Path.GetFileName(folder)}/{Path.GetFileName(p)}")
                })
                .OrderByDescending(f => f.SizeBytes)
                .ToList();

            ViewBag.Message = msg;
            ViewBag.Error = err;
            return View(files);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Upload(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return RedirectToAction(nameof(Index), new { err = "Nie wybrano pliku." });

            if (file.Length > 25 * 1024 * 1024)
                return RedirectToAction(nameof(Index), new { err = "Plik jest zbyt duży (limit 25 MB)." });

            var folder = GetUserFolder();
            var safeName = Regex.Replace(Path.GetFileName(file.FileName), @"[^a-zA-Z0-9_.-]", "_");
            var dest = Path.Combine(folder, safeName);
            using (var stream = System.IO.File.Create(dest))
                await file.CopyToAsync(stream);

            return RedirectToAction(nameof(Index), new { msg = $"Przesłano: {safeName}" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Delete(string name)
        {
            var safeName = Regex.Replace(Path.GetFileName(name ?? ""), @"[^a-zA-Z0-9_.-]", "_");
            var path = Path.Combine(GetUserFolder(), safeName);
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            return RedirectToAction(nameof(Index), new { msg = $"Usunięto: {safeName}" });
        }

        public class DocItem { public string Name { get; set; } = ""; public long SizeBytes { get; set; } public string Url { get; set; } = ""; }
    }
}