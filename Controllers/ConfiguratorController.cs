using DocumentFormat.OpenXml.Vml.Spreadsheet;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MonitoringConfigurator.Data;
using MonitoringConfigurator.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

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

        // Metoda GeneratePdf pozostaje podobna do poprzednich, uwzględniając nowe pola
        // ... (Skrót dla czytelności, logika taka sama jak wcześniej)
    }
}