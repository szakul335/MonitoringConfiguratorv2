using DocumentFormat.OpenXml.Vml.Spreadsheet;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MonitoringConfigurator.Data;
using MonitoringConfigurator.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Text.Json;

namespace MonitoringConfigurator.Controllers
{
    public class ConfiguratorController : Controller
    {
        private readonly AppDbContext _ctx;

        public ConfiguratorController(AppDbContext ctx)
        {
            _ctx = ctx;
        }

        [HttpGet]
        public IActionResult Index()
        {
            return View(new ConfiguratorInputModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Calculate(ConfiguratorInputModel input)
        {
            if (input.TotalCameras <= 0)
                ModelState.AddModelError("", "Wybierz przynajmniej jedną kamerę.");

            if (!ModelState.IsValid) return View("Index", input);

            var result = new ConfigurationResult { Input = input };

            // ==========================
            // 1. DOBÓR KAMER
            // ==========================

            // Budowanie bazowego zapytania w zależności od technologii
            var baseCamQuery = _ctx.Products
                .Where(p => p.Category == ProductCategory.Camera
                       && p.ResolutionMp >= input.ResolutionMp
                       && (p.IrRangeM == null || p.IrRangeM >= input.NightVisionM)); // IR min. tyle ile wybrano

            // Filtr technologii (uproszczony po nazwach/opisach)
            if (input.Tech == CameraTechnology.IpPoe)
            {
                // Szukamy kamer IP (często mają PoE w parametrach lub IP w nazwie)
                baseCamQuery = baseCamQuery.Where(p => p.Name.Contains("IP") || p.Description.Contains("IP") || p.PoeBudgetW > 0);
            }
            else if (input.Tech == CameraTechnology.Wifi)
            {
                baseCamQuery = baseCamQuery.Where(p => p.Name.Contains("Wi-Fi") || p.Description.Contains("Wi-Fi"));
            }
            else if (input.Tech == CameraTechnology.Analog)
            {
                baseCamQuery = baseCamQuery.Where(p => p.Name.Contains("Analog") || p.Name.Contains("HDCVI") || p.Name.Contains("TVI") || p.Name.Contains("AHD"));
            }

            // Filtr detekcji (AI) - jeśli wybrano AI, szukamy kamer "Smart", "AI", "Sense"
            if (input.Detection == DetectionType.Ai)
            {
                baseCamQuery = baseCamQuery.Where(p => p.Description.Contains("AI") || p.Description.Contains("Smart") || p.Description.Contains("Deep"));
            }

            // A. Kamery Zewnętrzne
            if (input.OutdoorCamCount > 0)
            {
                result.SelectedOutdoorCam = await baseCamQuery
                    .Where(p => p.Outdoor == true)
                    .OrderBy(p => p.Price)
                    .FirstOrDefaultAsync();
            }

            // B. Kamery Wewnętrzne
            if (input.IndoorCamCount > 0)
            {
                result.SelectedIndoorCam = await baseCamQuery
                    .OrderBy(p => p.Price) // Do środka pasują wszystkie (również te outdoor, bierzemy najtańszą)
                    .FirstOrDefaultAsync();
            }

            // ==========================
            // 2. OBLICZENIA TECHNICZNE
            // ==========================
            double bitrate = input.ResolutionMp * 1.5; // Mbps na kamerę
            result.EstimatedBandwidthMbps = Math.Round(bitrate * input.TotalCameras, 2);

            // Moc (jeśli PoE)
            int powerW = 0;
            if (input.Tech == CameraTechnology.IpPoe)
            {
                powerW = (input.OutdoorCamCount * (result.SelectedOutdoorCam?.PoeBudgetW ?? 12)) +
                         (input.IndoorCamCount * (result.SelectedIndoorCam?.PoeBudgetW ?? 8));
            }
            result.EstimatedPowerW = powerW;

            // Dysk
            double motionFactor = input.Building == BuildingType.Warehouse ? 0.2 : 0.45;
            if (input.Detection == DetectionType.Ai) motionFactor *= 0.8; // AI nagrywa precyzyjniej = mniej fałszywych alarmów = mniej dysku

            double dailyGB = (bitrate / 8) * 3600 * 24 / 1024;
            double realDailyGB = dailyGB * (0.3 + motionFactor);
            result.EstimatedStorageTB = Math.Round((realDailyGB * input.RecordingDays * input.TotalCameras) / 1024, 2);

            // ==========================
            // 3. REJESTRATOR (NVR/DVR)
            // ==========================
            var recQuery = _ctx.Products.Where(p => p.Category == ProductCategory.Recorder && p.Channels >= input.TotalCameras);

            if (input.Tech == CameraTechnology.Analog)
            {
                // Szukamy DVR
                recQuery = recQuery.Where(p => p.Name.Contains("DVR") || p.Name.Contains("XVR") || p.Description.Contains("Analog"));
            }
            else
            {
                // Szukamy NVR (IP)
                recQuery = recQuery.Where(p => (p.Name.Contains("NVR") || p.Description.Contains("IP")) && p.MaxBandwidthMbps >= result.EstimatedBandwidthMbps);
            }

            result.SelectedRecorder = await recQuery.OrderBy(p => p.Price).FirstOrDefaultAsync();

            // ==========================
            // 4. DYSK TWARDY
            // ==========================
            var maxHdd = await _ctx.Products.Where(p => p.Category == ProductCategory.Disk).OrderByDescending(p => p.StorageTB).FirstOrDefaultAsync();
            if (maxHdd != null && maxHdd.StorageTB > 0)
            {
                int needed = (int)Math.Ceiling(result.EstimatedStorageTB / maxHdd.StorageTB.Value);
                int slots = result.SelectedRecorder?.DiskBays ?? 1;
                result.DiskQuantity = Math.Min(needed, slots);
                result.SelectedDisk = maxHdd;
            }

            // ==========================
            // 5. SWITCH (Tylko dla IP PoE)
            // ==========================
            if (input.Tech == CameraTechnology.IpPoe) // Tylko PoE potrzebuje switcha PoE
            {
                result.SelectedSwitch = await _ctx.Products
                    .Where(p => p.Category == ProductCategory.Switch
                           && p.Ports >= input.TotalCameras
                           && p.PoeBudgetW >= result.EstimatedPowerW)
                    .OrderBy(p => p.Price).FirstOrDefaultAsync();

                if (result.SelectedSwitch != null) result.SwitchQuantity = 1;
            }

            // ==========================
            // 6. MONITOR / TV
            // ==========================
            if (input.DisplayMethod != DisplayType.AppOnly)
            {
                string searchKey = input.DisplayMethod == DisplayType.Tv ? "Telewizor" : "Monitor";
                result.SelectedMonitor = await _ctx.Products
                    .Where(p => p.Category == ProductCategory.Accessory && (p.Name.Contains(searchKey) || p.Name.Contains("Ekran")))
                    .OrderBy(p => p.Price).FirstOrDefaultAsync();

                if (result.SelectedMonitor != null) result.MonitorQuantity = 1;
            }

            // ==========================
            // 7. AKCESORIA & MONTAŻ
            // ==========================

            // Kabel
            double side = Math.Sqrt(input.AreaM2);
            int totalMeters = (int)(side * 2.0 * input.TotalCameras);
            result.EstimatedCableMeters = totalMeters;

            if (input.NeedCabling)
            {
                string cableType = input.Tech == CameraTechnology.Analog ? "Koncentry" : "Skrętka"; // Uproszczenie
                var cable = await _ctx.Products
                    .Where(p => p.Category == ProductCategory.Cable && p.Name.Contains(input.Tech == CameraTechnology.Analog ? "RG" : "UTP") && p.RollLengthM > 0)
                    .OrderBy(p => p.Price).FirstOrDefaultAsync();

                // Fallback jeśli nie znajdzie specyficznego
                if (cable == null) cable = await _ctx.Products.Where(p => p.Category == ProductCategory.Cable).OrderBy(p => p.Price).FirstOrDefaultAsync();

                if (cable != null)
                {
                    result.SelectedCable = cable;
                    result.CableQuantity = (int)Math.Ceiling((double)totalMeters / (cable.RollLengthM ?? 100));
                }
            }

            // Uchwyty / Puszki montażowe (1 na kamerę)
            result.SelectedMount = await _ctx.Products
                .Where(p => p.Category == ProductCategory.Accessory && (p.Name.Contains("Uchwyt") || p.Name.Contains("Puszka") || p.Name.Contains("Adapt")))
                .OrderBy(p => p.Price).FirstOrDefaultAsync();
            if (result.SelectedMount != null) result.MountQuantity = input.TotalCameras;

            // Wkręty / Kołki
            result.SelectedScrews = await _ctx.Products
                .Where(p => p.Category == ProductCategory.Accessory && (p.Name.Contains("Wkręt") || p.Name.Contains("Koł")))
                .OrderBy(p => p.Price).FirstOrDefaultAsync();
            if (result.SelectedScrews != null) result.ScrewsQuantity = (int)Math.Ceiling((input.TotalCameras * 4) / 50.0);

            // Korytka (dla natynkowej)
            if (input.InstallType == InstallationType.Surface)
            {
                result.SelectedTray = await _ctx.Products
                    .Where(p => p.Category == ProductCategory.Accessory && (p.Name.Contains("Koryt") || p.Name.Contains("Listwa")))
                    .OrderBy(p => p.Price).FirstOrDefaultAsync();
                if (result.SelectedTray != null) result.TrayMeters = totalMeters;
            }

            // UPS
            if (input.NeedUps)
            {
                int loadW = result.EstimatedPowerW + 40;
                double timeFactor = input.UpsRuntimeMinutes <= 15 ? 1.5 : (input.UpsRuntimeMinutes / 10.0);
                int neededVA = (int)((loadW / 0.6) * timeFactor);

                result.SelectedUps = await _ctx.Products
                    .Where(p => p.Category == ProductCategory.Ups && p.UpsVA >= neededVA)
                    .OrderBy(p => p.Price).FirstOrDefaultAsync();

                if (result.SelectedUps != null) result.UpsQuantity = 1;
            }

            // USŁUGA
            if (input.NeedAssembly)
            {
                result.AssemblyCost = (input.TotalCameras * 250.00m) + 300.00m + (input.InstallType == InstallationType.Flush ? 500.00m : 0m); // Podtynkowa droższa
            }

            return View("Summary", result);
        }

        // ==================================================================
        // METODA GENEROWANIA PDF I ZAPISU DO "MOJE DOKUMENTY"
        // ==================================================================
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

            // Konfiguracja licencji QuestPDF
            QuestPDF.Settings.License = LicenseType.Community;

            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Margin(40);
                    page.Size(PageSizes.A4);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(11).FontFamily("Arial"));

                    // --- NAGŁÓWEK ---
                    page.Header().Row(row =>
                    {
                        row.RelativeItem().Column(col =>
                        {
                            col.Item().Text("Oferta Systemu Monitoringu").FontSize(22).Bold().FontColor(Colors.Red.Medium);
                            col.Item().Text($"Dla obiektu: {config.Input.Building}").FontSize(14).FontColor(Colors.Grey.Darken2);
                        });
                        row.ConstantItem(100).AlignRight().Text(DateTime.Now.ToString("dd.MM.yyyy"));
                    });

                    // --- TREŚĆ ---
                    page.Content().PaddingVertical(20).Column(col =>
                    {
                        col.Item().Container().Background(Colors.Grey.Lighten4).Padding(10).Row(row =>
                        {
                            row.RelativeItem().Text($"Technologia: {config.Input.Tech}").SemiBold();
                            row.RelativeItem().Text($"Liczba kamer: {config.Input.TotalCameras}").SemiBold();
                            row.RelativeItem().Text($"Archiwizacja: {config.EstimatedStorageTB} TB").SemiBold();
                        });

                        col.Item().PaddingVertical(15).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

                        // Tabela produktów
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
                                header.Cell().AlignRight().Text("Cena jedn.").Bold();
                                header.Cell().AlignRight().Text("Wartość").Bold();
                            });

                            void AddRow(Product? p, int qty)
                            {
                                if (p != null && qty > 0)
                                {
                                    table.Cell().PaddingVertical(5).Text(p.Name + " (" + p.Model + ")");
                                    table.Cell().PaddingVertical(5).AlignRight().Text(qty.ToString());
                                    table.Cell().PaddingVertical(5).AlignRight().Text($"{p.Price:N2} zł");
                                    table.Cell().PaddingVertical(5).AlignRight().Text($"{(p.Price * qty):N2} zł").Bold();
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
                                table.Cell().PaddingVertical(5).AlignRight().Text($"{config.AssemblyCost:N2} zł");
                                table.Cell().PaddingVertical(5).AlignRight().Text($"{config.AssemblyCost:N2} zł").Bold();
                            }

                            table.Footer(footer =>
                            {
                                footer.Cell().ColumnSpan(4).PaddingTop(10).LineHorizontal(1).LineColor(Colors.Grey.Darken1);
                                footer.Cell().ColumnSpan(3).AlignRight().PaddingTop(5).Text("SUMA CAŁKOWITA (BRUTTO):").FontSize(14).Bold();
                                footer.Cell().AlignRight().PaddingTop(5).Text($"{config.TotalPrice:N2} zł").FontSize(14).Bold().FontColor(Colors.Red.Medium);
                            });
                        });
                    });

                    // --- STOPKA ---
                    page.Footer().AlignCenter().Text(x =>
                    {
                        x.Span("Wygenerowano automatycznie przez Konfigurator CCTV. ");
                        x.CurrentPageNumber();
                        x.Span(" / ");
                        x.TotalPages();
                    });
                });
            });

            // Generowanie PDF do strumienia pamięci
            var stream = new MemoryStream();
            document.GeneratePdf(stream);
            var contentBytes = stream.ToArray(); // Pobierz bajty do zapisu
            stream.Position = 0;

            // --- NOWA LOGIKA: Zapisz do bazy i na dysk, jeśli użytkownik jest zalogowany ---
            if (User.Identity?.IsAuthenticated == true)
            {
                var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                var userName = User.Identity.Name;

                if (!string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(userName))
                {
                    // 1. Zapis fizyczny na dysku (opcjonalny, ale przydatny dla DocumentsController)
                    try
                    {
                        var basePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "documents");
                        // Bezpieczna nazwa folderu użytkownika
                        var safeUserName = System.Text.RegularExpressions.Regex.Replace(userName, @"[^a-zA-Z0-9_.@-]", "_");
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
                            InputJson = jsonResult,
                            ResultJson = jsonResult // Można zapisać wynik dla historii
                        };

                        _ctx.UserDocuments.Add(userDoc);
                        await _ctx.SaveChangesAsync();
                    }
                    catch (Exception ex)
                    {
                        // Logowanie błędu, ale nie przerywamy pobierania pliku
                        Console.WriteLine($"Błąd zapisu dokumentu: {ex.Message}");
                    }
                }
            }

            string downloadName = $"Oferta_{DateTime.Now:yyyyMMdd_HHmm}.pdf";
            return File(stream, "application/pdf", downloadName);
        }

        // Metoda GeneratePdf pozostaje podobna do poprzednich, uwzględniając nowe pola
        // ... (Skrót dla czytelności, logika taka sama jak wcześniej)
    }
}