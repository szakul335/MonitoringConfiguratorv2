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

            // ==================================================================
            // 1. DOBÓR KAMER (Wg pola ShortDescription = 'IP' / 'WIFI' / 'ANALOG')
            // ==================================================================
            string techKey = input.Tech switch
            {
                CameraTechnology.IpPoe => "IP",
                CameraTechnology.Wifi => "WIFI",
                CameraTechnology.Analog => "ANALOG",
                _ => "IP"
            };

            // Zapytanie bazowe - filtrujemy po technologii zapisanej w ShortDescription
            var baseCamQuery = _ctx.Products
                .Where(p => p.Category == ProductCategory.Camera
                       && p.ResolutionMp >= input.ResolutionMp
                       && (p.ShortDescription == techKey)
                       && (p.IrRangeM == null || p.IrRangeM >= input.NightVisionM));

            // Filtr detekcji AI
            if (input.Detection == DetectionType.Ai)
            {
                baseCamQuery = baseCamQuery.Where(p => p.Description.Contains("AI") || p.Name.Contains("AI"));
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
                    .OrderBy(p => p.Price)
                    .FirstOrDefaultAsync();
            }

            // ==========================
            // 2. OBLICZENIA TECHNICZNE
            // ==========================
            double bitrate = input.ResolutionMp * 1.5;
            result.EstimatedBandwidthMbps = Math.Round(bitrate * input.TotalCameras, 2);

            int powerW = 0;
            if (input.Tech == CameraTechnology.IpPoe)
            {
                powerW = (input.OutdoorCamCount * (result.SelectedOutdoorCam?.PoeBudgetW ?? 12)) +
                         (input.IndoorCamCount * (result.SelectedIndoorCam?.PoeBudgetW ?? 8));
            }
            result.EstimatedPowerW = powerW;

            double dailyGB = (bitrate / 8) * 3600 * 24 / 1024;
            // Magazyn: mniejszy ruch = mniej zajętego miejsca
            double motionFactor = input.Building == BuildingType.Warehouse ? 0.2 : 0.45;
            double realDailyGB = dailyGB * (0.3 + motionFactor);
            result.EstimatedStorageTB = Math.Round((realDailyGB * input.RecordingDays * input.TotalCameras) / 1024, 2);

            // ==========================
            // 3. REJESTRATOR (NVR/DVR)
            // ==========================
            // Rejestrator też musi mieć pasujący ShortDescription (np. "IP" lub "ANALOG") 
            // albo "UNIWERSALNY" jeśli obsługuje wszystko.
            var recQuery = _ctx.Products
                .Where(p => p.Category == ProductCategory.Recorder
                       && p.Channels >= input.TotalCameras
                       && (p.ShortDescription == techKey || p.ShortDescription == "UNIWERSALNY"));

            if (input.Tech != CameraTechnology.Analog)
            {
                // Dla IP/WIFI sprawdzamy przepustowość
                recQuery = recQuery.Where(p => p.MaxBandwidthMbps >= result.EstimatedBandwidthMbps);
            }

            result.SelectedRecorder = await recQuery.OrderBy(p => p.Price).FirstOrDefaultAsync();

            // ==========================
            // 4. DYSK TWARDY
            // ==========================
            var maxHdd = await _ctx.Products
                .Where(p => p.Category == ProductCategory.Disk)
                .OrderByDescending(p => p.StorageTB).FirstOrDefaultAsync();

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
            if (input.Tech == CameraTechnology.IpPoe)
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
                    .Where(p => p.Category == ProductCategory.Accessory
                           && (p.Name.Contains(searchKey) || p.Name.Contains("Ekran") || p.ShortDescription == "UNIWERSALNY"))
                    .OrderBy(p => p.Price).FirstOrDefaultAsync();

                if (result.SelectedMonitor != null) result.MonitorQuantity = 1;
            }

            // ==========================
            // 7. AKCESORIA & MONTAŻ
            // ==========================
            double side = Math.Sqrt(input.AreaM2);
            int totalMeters = (int)(side * 2.0 * input.TotalCameras);
            result.EstimatedCableMeters = totalMeters;

            if (input.NeedCabling)
            {
                // Dobieramy kabel po nazwie lub opisie uniwersalnym
                string cableKey = input.Tech == CameraTechnology.Analog ? "Koncentryczny" : "Skrętka";
                if (input.Tech == CameraTechnology.Wifi) cableKey = "Zasilający";

                var cable = await _ctx.Products
                    .Where(p => p.Category == ProductCategory.Cable
                           && (p.Name.Contains(cableKey) || p.Name.Contains("Kabel") || p.ShortDescription == "UNIWERSALNY"))
                    .OrderBy(p => p.Price).FirstOrDefaultAsync();

                if (cable != null)
                {
                    result.SelectedCable = cable;
                    result.CableQuantity = (int)Math.Ceiling((double)totalMeters / (cable.RollLengthM ?? 100));
                }
            }

            // Uchwyty
            result.SelectedMount = await _ctx.Products
                .Where(p => p.Category == ProductCategory.Accessory && (p.Name.Contains("Puszka") || p.Name.Contains("Uchwyt")))
                .OrderBy(p => p.Price).FirstOrDefaultAsync();
            if (result.SelectedMount != null) result.MountQuantity = input.TotalCameras;

            // Korytka
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
                result.SelectedUps = await _ctx.Products
                    .Where(p => p.Category == ProductCategory.Ups && p.UpsVA > loadW)
                    .OrderBy(p => p.Price).FirstOrDefaultAsync();

                if (result.SelectedUps != null) result.UpsQuantity = 1;
            }

            // USŁUGA
            if (input.NeedAssembly)
            {
                result.AssemblyCost = (input.TotalCameras * 250.00m) + 300.00m + (input.InstallType == InstallationType.Flush ? 500.00m : 0m);
            }

            return View("Summary", result);
        }

        // Metoda GeneratePdf pozostawiona bez zmian (zakładamy, że już jest zaimplementowana w Twoim kodzie)
        [HttpPost]
        public IActionResult GeneratePdf(string jsonResult)
        {
            // ... (implementacja generowania PDF) ...
            return RedirectToAction("Index"); // Placeholder
        }
    }
}