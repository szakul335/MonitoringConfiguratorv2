using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MonitoringConfigurator.Data;
using MonitoringConfigurator.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Text.Json;
using System.IO;
using System.Text.RegularExpressions;

namespace MonitoringConfigurator.Controllers
{
    public class ConfiguratorController : Controller
    {
        private readonly AppDbContext _ctx;
        private readonly IWebHostEnvironment _env;

        public ConfiguratorController(AppDbContext ctx, IWebHostEnvironment env)
        {
            _ctx = ctx;
            _env = env;
        }

        [HttpGet]
        public IActionResult Index() => View(new ConfiguratorInputModel());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Calculate(ConfiguratorInputModel input)
        {
            if (input.TotalCameras <= 0)
                ModelState.AddModelError("", "Wybierz przynajmniej jedną kamerę.");

            if (!ModelState.IsValid) return View("Index", input);

            var result = new ConfigurationResult { Input = input };

            // 1. DOBÓR KAMER
            string techKey = input.Tech switch
            {
                CameraTechnology.IpPoe => "IP",
                CameraTechnology.Wifi => "WIFI",
                CameraTechnology.Analog => "ANALOG",
                _ => "IP"
            };

            var baseCamQuery = _ctx.Products
                .Where(p => p.Category == ProductCategory.Camera
                       && p.ResolutionMp >= input.ResolutionMp
                       && (p.ShortDescription == techKey)
                       && (p.IrRangeM == null || p.IrRangeM >= input.NightVisionM));

            if (input.Detection == DetectionType.Ai)
                baseCamQuery = baseCamQuery.Where(p => p.Description.Contains("AI") || p.Name.Contains("AI"));

            if (input.OutdoorCamCount > 0)
                result.SelectedOutdoorCam = await baseCamQuery.Where(p => p.Outdoor == true).OrderBy(p => p.Price).FirstOrDefaultAsync();

            if (input.IndoorCamCount > 0)
                result.SelectedIndoorCam = await baseCamQuery.OrderBy(p => p.Price).FirstOrDefaultAsync();

            // 2. OBLICZENIA
            double bitrate = input.ResolutionMp * 1.5;
            result.EstimatedBandwidthMbps = Math.Round(bitrate * input.TotalCameras, 2);

            int powerW = 0;
            if (input.Tech == CameraTechnology.IpPoe)
                powerW = (input.OutdoorCamCount * (result.SelectedOutdoorCam?.PoeBudgetW ?? 10)) +
                         (input.IndoorCamCount * (result.SelectedIndoorCam?.PoeBudgetW ?? 5));
            result.EstimatedPowerW = powerW;

            // DYSK
            double dailyGB = (bitrate / 8) * 3600 * 24 / 1024;
            double motionFactor = input.Building == BuildingType.Warehouse ? 0.2 : 0.45;
            result.EstimatedStorageTB = Math.Round((dailyGB * (0.3 + motionFactor) * input.RecordingDays * input.TotalCameras) / 1024, 2);

            // 3. REJESTRATOR
            var recQuery = _ctx.Products.Where(p => p.Category == ProductCategory.Recorder
                && p.Channels >= input.TotalCameras
                && (p.ShortDescription == techKey || p.ShortDescription == "UNIWERSALNY"));

            if (input.Tech != CameraTechnology.Analog)
                recQuery = recQuery.Where(p => p.MaxBandwidthMbps >= result.EstimatedBandwidthMbps);

            result.SelectedRecorder = await recQuery.OrderBy(p => p.Price).FirstOrDefaultAsync();

            // 4. DOBÓR DYSKU
            var allDisks = await _ctx.Products
                .Where(p => p.Category == ProductCategory.Disk && (p.ShortDescription == "UNIWERSALNY" ))
                .OrderBy(p => p.Price).ToListAsync();

            if (allDisks.Any())
            {
                var perfectFit = allDisks.FirstOrDefault(d => (d.StorageTB ?? 0) >= result.EstimatedStorageTB);
                if (perfectFit != null)
                {
                    result.SelectedDisk = perfectFit; result.DiskQuantity = 1;
                }
                else
                {
                    var maxDisk = allDisks.OrderByDescending(d => d.StorageTB).First();
                    int needed = (int)Math.Ceiling(result.EstimatedStorageTB / (maxDisk.StorageTB ?? 1));
                    int slots = result.SelectedRecorder?.DiskBays ?? 1;
                    result.SelectedDisk = maxDisk; result.DiskQuantity = Math.Min(needed, slots);
                }
            }

            // 5. SWITCH
            if (input.Tech == CameraTechnology.IpPoe)
            {
                result.SelectedSwitch = await _ctx.Products
                    .Where(p => p.Category == ProductCategory.Switch && p.Ports >= input.TotalCameras && p.PoeBudgetW >= result.EstimatedPowerW)
                    .OrderBy(p => p.Price).FirstOrDefaultAsync();
                if (result.SelectedSwitch != null) result.SwitchQuantity = 1;
            }

            // 6. AKCESORIA & KABEL (Z uwzględnieniem wizualizatora!)
            if (input.NeedCabling)
            {
                string cName = input.Tech == CameraTechnology.Analog ? "Koncentryczny" : "Skrętka";
                if (input.Tech == CameraTechnology.Wifi) cName = "Zasilający";

                result.SelectedCable = await _ctx.Products.Where(p => p.Category == ProductCategory.Cable && (p.Name.Contains(cName) || p.ShortDescription == "UNIWERSALNY")).OrderBy(p => p.Price).FirstOrDefaultAsync();

                if (result.SelectedCable != null)
                {
                    // --- TUTAJ ZMIANA: Sprawdzamy czy użytkownik narysował plan ---
                    int meters;
                    if (input.CustomCableMeters.HasValue && input.CustomCableMeters > 0)
                    {
                        meters = input.CustomCableMeters.Value; // Użyj wartości z wizualizatora
                    }
                    else
                    {
                        double side = Math.Sqrt(input.AreaM2); // Estymacja matematyczna
                        meters = (int)(side * 2.0 * input.TotalCameras);
                    }

                    result.EstimatedCableMeters = meters;
                    result.CableQuantity = (int)Math.Ceiling((double)meters / (result.SelectedCable.RollLengthM ?? 100));
                }
            }

            // Uchwyty
            result.SelectedMount = await _ctx.Products.Where(p => p.Category == ProductCategory.Accessory && p.Name.Contains("Puszka")).OrderBy(p => p.Price).FirstOrDefaultAsync();
            if (result.SelectedMount != null) result.MountQuantity = input.TotalCameras;

            // Monitor, UPS, Montaż... (bez zmian)
            if (input.DisplayMethod != DisplayType.AppOnly)
            {
                string mKey = input.DisplayMethod == DisplayType.Tv ? "Telewizor" : "Monitor";
                result.SelectedMonitor = await _ctx.Products.Where(p => p.Category == ProductCategory.Accessory && p.Name.Contains(mKey)).OrderBy(p => p.Price).FirstOrDefaultAsync();
                if (result.SelectedMonitor != null) result.MonitorQuantity = 1;
            }
            if (input.NeedUps)
            {
                int load = result.EstimatedPowerW + 40;
                result.SelectedUps = await _ctx.Products.Where(p => p.Category == ProductCategory.Ups && p.UpsVA > load).OrderBy(p => p.Price).FirstOrDefaultAsync();
                if (result.SelectedUps != null) result.UpsQuantity = 1;
            }
            if (input.NeedAssembly)
                result.AssemblyCost = (input.TotalCameras * 200.00m) + 500.00m;

            return View("Summary", result);
        }

        // --- GENERATE PDF (Wersja Single Result z zapytania o "Moje dokumenty") ---
        [HttpPost]
        public async Task<IActionResult> GeneratePdf(string jsonResult)
        {
            if (string.IsNullOrEmpty(jsonResult)) return RedirectToAction("Index");
            ConfigurationResult? config;
            try { config = JsonSerializer.Deserialize<ConfigurationResult>(jsonResult); }
            catch { return RedirectToAction("Index"); }
            if (config == null) return RedirectToAction("Index");

            QuestPDF.Settings.License = LicenseType.Community;
            var document = Document.Create(container => {
                container.Page(page => {
                    page.Margin(40);
                    page.Size(PageSizes.A4);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(11).FontFamily("Arial"));
                    page.Header().Row(row => {
                        row.RelativeItem().Column(col => {
                            col.Item().Text("Oferta Systemu Monitoringu").FontSize(22).Bold().FontColor(Colors.Red.Medium);
                            col.Item().Text($"Obiekt: {config.Input.Building}").FontSize(14).FontColor(Colors.Grey.Darken2);
                        });
                        row.ConstantItem(100).AlignRight().Text(DateTime.Now.ToString("dd.MM.yyyy"));
                    });
                    page.Content().PaddingVertical(20).Column(col => {
                        col.Item().Container().Background(Colors.Grey.Lighten4).Padding(10).Row(row => {
                            row.RelativeItem().Text($"Technologia: {config.Input.Tech}").SemiBold();
                            row.RelativeItem().Text($"Kamery: {config.Input.TotalCameras}").SemiBold();
                            row.RelativeItem().Text($"Kabel: {config.EstimatedCableMeters} m").SemiBold(); // Pokaż metry
                        });
                        col.Item().PaddingVertical(15).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

                        col.Item().Table(table => {
                            table.ColumnsDefinition(c => { c.RelativeColumn(4); c.RelativeColumn(1); c.RelativeColumn(2); c.RelativeColumn(2); });
                            table.Header(h => { h.Cell().Text("Produkt").Bold(); h.Cell().AlignRight().Text("Ilość").Bold(); h.Cell().AlignRight().Text("Cena").Bold(); h.Cell().AlignRight().Text("Wartość").Bold(); });

                            void AddRow(Product? p, int q)
                            {
                                if (p != null && q > 0)
                                {
                                    table.Cell().Text(p.Name); table.Cell().AlignRight().Text(q.ToString());
                                    table.Cell().AlignRight().Text($"{p.Price:N2}"); table.Cell().AlignRight().Text($"{(p.Price * q):N2}").Bold();
                                }
                            }
                            AddRow(config.SelectedOutdoorCam, config.Input.OutdoorCamCount);
                            AddRow(config.SelectedIndoorCam, config.Input.IndoorCamCount);
                            AddRow(config.SelectedRecorder, config.RecorderQuantity);
                            AddRow(config.SelectedDisk, config.DiskQuantity);
                            AddRow(config.SelectedSwitch, config.SwitchQuantity);
                            AddRow(config.SelectedCable, config.CableQuantity);
                            AddRow(config.SelectedMount, config.MountQuantity);
                            AddRow(config.SelectedMonitor, config.MonitorQuantity);
                            AddRow(config.SelectedUps, config.UpsQuantity);
                            if (config.AssemblyCost > 0)
                            {
                                table.Cell().Text("Usługa montażu"); table.Cell().AlignRight().Text("1");
                                table.Cell().AlignRight().Text($"{config.AssemblyCost:N2}"); table.Cell().AlignRight().Text($"{config.AssemblyCost:N2}").Bold();
                            }
                            table.Footer(f => {
                                f.Cell().ColumnSpan(4).AlignRight().PaddingTop(10).Text($"SUMA: {config.TotalPrice:N2} zł").FontSize(14).Bold().FontColor(Colors.Red.Medium);
                            });
                        });
                    });
                    page.Footer().AlignCenter().Text(x => { x.Span("Generowano automatycznie. "); x.CurrentPageNumber(); });
                });
            });

            var stream = new MemoryStream();
            document.GeneratePdf(stream);
            var bytes = stream.ToArray();
            stream.Position = 0;

            if (User.Identity?.IsAuthenticated == true)
            {
                var uid = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (uid != null)
                {
                    try
                    {
                        var path = Path.Combine(_env.WebRootPath, "documents", Regex.Replace(User.Identity.Name ?? "user", @"[^a-zA-Z0-9_.@-]", "_"));
                        Directory.CreateDirectory(path);
                        await System.IO.File.WriteAllBytesAsync(Path.Combine(path, $"Oferta_{DateTime.Now:yyyyMMdd_HHmmss}.pdf"), bytes);
                        _ctx.UserDocuments.Add(new UserDocument { UserId = uid, Title = $"Oferta {config.Input.Tech}", Format = "pdf", Content = bytes, InputJson = jsonResult });
                        await _ctx.SaveChangesAsync();
                    }
                    catch { }
                }
            }
            return File(stream, "application/pdf", "Oferta.pdf");
        }
    }
}