using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MonitoringConfigurator.Data;
using MonitoringConfigurator.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Text.Json;
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

        // --- 1. WYŚWIETLANIE PUSTEGO KONFIGURATORA ---
        [HttpGet]
        public IActionResult Index()
        {
            return View(new ConfiguratorInputModel());
        }

        // --- 2. WCZYTYWANIE ZAPISANEJ KONFIGURACJI (Z "MOJE DOKUMENTY") ---
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> Load(int id)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (userId == null) return RedirectToAction("Index");

            // Pobierz dokument z bazy
            var doc = await _ctx.UserDocuments.FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId);

            if (doc == null || string.IsNullOrEmpty(doc.InputJson))
            {
                TempData["Error"] = "Nie znaleziono zapisanych danych dla tego projektu.";
                return RedirectToAction("Index", "Documents");
            }

            // Deserializacja danych wejściowych
            ConfiguratorInputModel? inputModel;
            try
            {
                inputModel = JsonSerializer.Deserialize<ConfiguratorInputModel>(doc.InputJson);
            }
            catch
            {
                return RedirectToAction("Index", "Documents");
            }

            if (inputModel == null) return RedirectToAction("Index");

            // Załaduj widok z wypełnionymi danymi
            return View("Index", inputModel);
        }

        // --- 3. OBLICZANIE I DOBÓR SPRZĘTU ---
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Calculate(ConfiguratorInputModel input)
        {
            if (input.TotalCameras <= 0)
                ModelState.AddModelError("", "Wybierz przynajmniej jedną kamerę.");

            if (!ModelState.IsValid) return View("Index", input);

            var result = new ConfigurationResult { Input = input };

            // A. DOBÓR KAMER
            // Ustalamy klucz wyszukiwania na podstawie wybranej technologii
            string techKey = input.Tech switch
            {
                CameraTechnology.IpPoe => "IP",
                CameraTechnology.Wifi => "WIFI",
                CameraTechnology.Analog => "ANALOG",
                _ => "IP"
            };

            // Bazowe zapytanie do bazy produktów
            var baseCamQuery = _ctx.Products
                .Where(p => p.Category == ProductCategory.Camera
                       && p.ResolutionMp >= input.ResolutionMp
                       && (p.ShortDescription == techKey) // Kluczowe filtrowanie po technologii
                       && (p.IrRangeM == null || p.IrRangeM >= input.NightVisionM));

            // Opcjonalny filtr AI
            if (input.Detection == DetectionType.Ai)
            {
                baseCamQuery = baseCamQuery.Where(p => p.Description.Contains("AI") || p.Name.Contains("AI"));
            }

            // Kamery Zewnętrzne
            if (input.OutdoorCamCount > 0)
            {
                result.SelectedOutdoorCam = await baseCamQuery
                    .Where(p => p.Outdoor == true)
                    .OrderBy(p => p.Price)
                    .FirstOrDefaultAsync();
            }

            // Kamery Wewnętrzne
            if (input.IndoorCamCount > 0)
            {
                result.SelectedIndoorCam = await baseCamQuery
                    .OrderBy(p => p.Price)
                    .FirstOrDefaultAsync();
            }

            // B. OBLICZENIA TECHNICZNE
            double bitrate = input.ResolutionMp * 1.5; // Szacunkowy bitrate (Mbps) na kamerę
            result.EstimatedBandwidthMbps = Math.Round(bitrate * input.TotalCameras, 2);

            // Moc (tylko dla IP PoE)
            int powerW = 0;
            if (input.Tech == CameraTechnology.IpPoe)
            {
                powerW = (input.OutdoorCamCount * (result.SelectedOutdoorCam?.PoeBudgetW ?? 10)) +
                         (input.IndoorCamCount * (result.SelectedIndoorCam?.PoeBudgetW ?? 5));
            }
            result.EstimatedPowerW = powerW;

            // Pojemność dysku
            double dailyGB = (bitrate / 8) * 3600 * 24 / 1024;
            double motionFactor = input.Building == BuildingType.Warehouse ? 0.2 : 0.45; // Ruch zależy od typu obiektu
            if (input.Detection == DetectionType.Ai) motionFactor *= 0.8; // AI redukuje fałszywe nagrania

            result.EstimatedStorageTB = Math.Round((dailyGB * (0.3 + motionFactor) * input.RecordingDays * input.TotalCameras) / 1024, 2);

            // C. DOBÓR REJESTRATORA
            var recQuery = _ctx.Products
                .Where(p => p.Category == ProductCategory.Recorder
                       && p.Channels >= input.TotalCameras
                       && (p.ShortDescription == techKey || p.ShortDescription == "UNIWERSALNY"));

            // Dla IP sprawdzamy też przepustowość (Bandwidth)
            if (input.Tech != CameraTechnology.Analog)
            {
                recQuery = recQuery.Where(p => p.MaxBandwidthMbps >= result.EstimatedBandwidthMbps);
            }

            result.SelectedRecorder = await recQuery.OrderBy(p => p.Price).FirstOrDefaultAsync();

            // D. DOBÓR DYSKU TWARDEGO
            var allDisks = await _ctx.Products
                .Where(p => p.Category == ProductCategory.Disk && (p.ShortDescription == "UNIWERSALNY"))
                .OrderBy(p => p.Price)
                .ToListAsync();

            if (allDisks.Any())
            {
                // Szukamy idealnego dysku (pojemność >= wymagana)
                var perfectFit = allDisks.FirstOrDefault(d => (d.StorageTB ?? 0) >= result.EstimatedStorageTB);

                if (perfectFit != null)
                {
                    result.SelectedDisk = perfectFit;
                    result.DiskQuantity = 1;
                }
                else
                {
                    // Jeśli jeden dysk to za mało, bierzemy największy i mnożymy
                    var maxDisk = allDisks.OrderByDescending(d => d.StorageTB).First();
                    int needed = (int)Math.Ceiling(result.EstimatedStorageTB / (maxDisk.StorageTB ?? 1));
                    int slots = result.SelectedRecorder?.DiskBays ?? 1; // Sprawdzamy ile slotów ma rejestrator

                    result.SelectedDisk = maxDisk;
                    result.DiskQuantity = Math.Min(needed, slots);
                }
            }

            // E. SWITCH (Tylko dla IP PoE)
            if (input.Tech == CameraTechnology.IpPoe)
            {
                result.SelectedSwitch = await _ctx.Products
                    .Where(p => p.Category == ProductCategory.Switch
                           && p.Ports >= input.TotalCameras
                           && p.PoeBudgetW >= result.EstimatedPowerW)
                    .OrderBy(p => p.Price).FirstOrDefaultAsync();

                if (result.SelectedSwitch != null) result.SwitchQuantity = 1;
            }

            // F. OKABLOWANIE I AKCESORIA
            if (input.NeedCabling)
            {
                string cableName = input.Tech == CameraTechnology.Analog ? "Koncentryczny" : "Skrętka";
                if (input.Tech == CameraTechnology.Wifi) cableName = "Zasilający";

                result.SelectedCable = await _ctx.Products
                    .Where(p => p.Category == ProductCategory.Cable && (p.Name.Contains(cableName) || p.ShortDescription == "UNIWERSALNY"))
                    .OrderBy(p => p.Price).FirstOrDefaultAsync();

                if (result.SelectedCable != null)
                {
                    int totalMeters;

                    // --- LOGIKA WIZUALIZATORA ---
                    // Jeśli użytkownik narysował plan, używamy dokładnej wartości
                    if (input.CustomCableMeters.HasValue && input.CustomCableMeters > 0)
                    {
                        totalMeters = input.CustomCableMeters.Value;
                    }
                    else
                    {
                        // W przeciwnym razie estymacja matematyczna z metrażu
                        double side = Math.Sqrt(input.AreaM2);
                        totalMeters = (int)(side * 2.0 * input.TotalCameras);
                    }

                    result.EstimatedCableMeters = totalMeters;
                    result.CableQuantity = (int)Math.Ceiling((double)totalMeters / (result.SelectedCable.RollLengthM ?? 100));
                }
            }

            // Uchwyty montażowe
            result.SelectedMount = await _ctx.Products
                .Where(p => p.Category == ProductCategory.Accessory && (p.Name.Contains("Puszka") || p.Name.Contains("Uchwyt")))
                .OrderBy(p => p.Price).FirstOrDefaultAsync();
            if (result.SelectedMount != null) result.MountQuantity = input.TotalCameras;

            // Monitor
            if (input.DisplayMethod != DisplayType.AppOnly)
            {
                string monitorKey = input.DisplayMethod == DisplayType.Tv ? "Telewizor" : "Monitor";
                result.SelectedMonitor = await _ctx.Products
                    .Where(p => p.Category == ProductCategory.Accessory && p.Name.Contains(monitorKey))
                    .OrderBy(p => p.Price).FirstOrDefaultAsync();
                if (result.SelectedMonitor != null) result.MonitorQuantity = 1;
            }

            // UPS (Zasilanie awaryjne)
            if (input.NeedUps)
            {
                int loadW = result.EstimatedPowerW + 40; // +40W na rejestrator
                result.SelectedUps = await _ctx.Products
                    .Where(p => p.Category == ProductCategory.Ups && p.UpsVA > loadW)
                    .OrderBy(p => p.Price).FirstOrDefaultAsync();
                if (result.SelectedUps != null) result.UpsQuantity = 1;
            }

            // Usługa montażu
            if (input.NeedAssembly)
            {
                result.AssemblyCost = (input.TotalCameras * 200.00m) + 500.00m;
            }

            return View("Summary", result);
        }

        // --- 4. GENEROWANIE PDF I ZAPIS DO HISTORII ---
        [HttpPost]
        public async Task<IActionResult> GeneratePdf(string jsonResult)
        {
            if (string.IsNullOrEmpty(jsonResult)) return RedirectToAction("Index");

            ConfigurationResult? config;
            try
            {
                config = JsonSerializer.Deserialize<ConfigurationResult>(jsonResult);
            }
            catch
            {
                return RedirectToAction("Index");
            }

            if (config == null) return RedirectToAction("Index");

            // Ustawienie licencji QuestPDF
            QuestPDF.Settings.License = LicenseType.Community;

            // Generowanie dokumentu
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Margin(40);
                    page.Size(PageSizes.A4);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(11).FontFamily("Arial"));

                    // Nagłówek
                    page.Header().Row(row =>
                    {
                        row.RelativeItem().Column(col =>
                        {
                            col.Item().Text("Oferta Systemu Monitoringu").FontSize(22).Bold().FontColor(Colors.Red.Medium);
                            col.Item().Text($"Dla obiektu: {config.Input.Building}").FontSize(14).FontColor(Colors.Grey.Darken2);
                        });
                        row.ConstantItem(100).AlignRight().Text(DateTime.Now.ToString("dd.MM.yyyy"));
                    });

                    // Treść
                    page.Content().PaddingVertical(20).Column(col =>
                    {
                        col.Item().Container().Background(Colors.Grey.Lighten4).Padding(10).Row(row =>
                        {
                            row.RelativeItem().Text($"Technologia: {config.Input.Tech}").SemiBold();
                            row.RelativeItem().Text($"Kamery: {config.Input.TotalCameras}").SemiBold();
                            row.RelativeItem().Text($"Kabel: {config.EstimatedCableMeters} m").SemiBold();
                        });

                        col.Item().PaddingVertical(15).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

                        // Tabela
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(4); // Nazwa
                                columns.RelativeColumn(1); // Ilość
                                columns.RelativeColumn(2); // Cena jedn.
                                columns.RelativeColumn(2); // Wartość
                            });

                            table.Header(header =>
                            {
                                header.Cell().Text("Produkt").Bold();
                                header.Cell().AlignRight().Text("Ilość").Bold();
                                header.Cell().AlignRight().Text("Cena").Bold();
                                header.Cell().AlignRight().Text("Wartość").Bold();
                            });

                            void AddRow(Product? p, int qty)
                            {
                                if (p != null && qty > 0)
                                {
                                    table.Cell().PaddingVertical(5).Text(p.Name + " (" + p.Model + ")");
                                    table.Cell().PaddingVertical(5).AlignRight().Text(qty.ToString());
                                    table.Cell().PaddingVertical(5).AlignRight().Text($"{p.Price:N2}");
                                    table.Cell().PaddingVertical(5).AlignRight().Text($"{(p.Price * qty):N2}").Bold();
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
                                table.Cell().PaddingVertical(5).Text("Usługa montażu i konfiguracji");
                                table.Cell().PaddingVertical(5).AlignRight().Text("1");
                                table.Cell().PaddingVertical(5).AlignRight().Text($"{config.AssemblyCost:N2}");
                                table.Cell().PaddingVertical(5).AlignRight().Text($"{config.AssemblyCost:N2}").Bold();
                            }

                            table.Footer(footer =>
                            {
                                footer.Cell().ColumnSpan(4).PaddingTop(10).LineHorizontal(1).LineColor(Colors.Grey.Darken1);
                                footer.Cell().ColumnSpan(3).AlignRight().PaddingTop(5).Text("SUMA CAŁKOWITA (BRUTTO):").FontSize(14).Bold();
                                footer.Cell().AlignRight().PaddingTop(5).Text($"{config.TotalPrice:N2} zł").FontSize(14).Bold().FontColor(Colors.Red.Medium);
                            });
                        });
                    });

                    // STOPKA Z KLAUZULĄ PRAWNĄ
                    page.Footer()
                        .PaddingTop(10)
                        .Column(column =>
                        {
                            column.Item().Text(text =>
                            {
                                text.Span("UWAGA: Przedstawiona kalkulacja ma charakter wyłącznie poglądowy i nie stanowi oferty handlowej w rozumieniu Art. 66 par. 1 Kodeksu Cywilnego. ")
                                    .FontSize(9).FontColor(Colors.Grey.Medium);

                                text.Span("Ceny mogą ulec zmianie. W celu uzyskania wiążącej wyceny oraz szczegółowych informacji prosimy o kontakt z działem handlowym pod numerem: ")
                                    .FontSize(9).FontColor(Colors.Grey.Medium);

                                text.Span("+48 123 456 789")
                                    .FontSize(9).FontColor(Colors.Red.Medium).Bold();
                            });

                            column.Item().PaddingTop(5).AlignCenter().Text(x =>
                            {
                                x.Span("Wygenerowano automatycznie. ");
                                x.CurrentPageNumber();
                                x.Span(" / ");
                                x.TotalPages();
                            });
                        });
                });
            });

            // Generowanie strumienia
            var stream = new MemoryStream();
            document.GeneratePdf(stream);
            var contentBytes = stream.ToArray();
            stream.Position = 0;

            // ZAPIS DOKUMENTU DLA ZALOGOWANEGO UŻYTKOWNIKA
            if (User.Identity?.IsAuthenticated == true)
            {
                var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                var userName = User.Identity.Name;

                if (!string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(userName))
                {
                    try
                    {
                        // 1. Zapis fizyczny na dysku
                        var basePath = Path.Combine(_env.WebRootPath, "documents");
                        var safeUserName = Regex.Replace(userName, @"[^a-zA-Z0-9_.@-]", "_");
                        var userFolder = Path.Combine(basePath, safeUserName);

                        if (!Directory.Exists(userFolder)) Directory.CreateDirectory(userFolder);

                        var fileName = $"Oferta_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
                        var filePath = Path.Combine(userFolder, fileName);

                        await System.IO.File.WriteAllBytesAsync(filePath, contentBytes);

                        // 2. Zapis w bazie danych (UserDocuments)
                        var userDoc = new UserDocument
                        {
                            UserId = userId,
                            Title = $"Oferta CCTV - {config.Input.Building} ({config.Input.Tech})",
                            Format = "pdf",
                            Content = contentBytes,
                            CreatedUtc = DateTime.UtcNow,

                            // Zapisujemy cały obiekt wejściowy (JSON), aby móc go później załadować (funkcja "Edytuj")
                            InputJson = JsonSerializer.Serialize(config.Input),
                            ResultJson = jsonResult
                        };

                        _ctx.UserDocuments.Add(userDoc);
                        await _ctx.SaveChangesAsync();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Błąd zapisu dokumentu: {ex.Message}");
                    }
                }
            }

            string downloadName = $"Oferta_{DateTime.Now:yyyyMMdd_HHmm}.pdf";
            return File(stream, "application/pdf", downloadName);
        }
    }
}