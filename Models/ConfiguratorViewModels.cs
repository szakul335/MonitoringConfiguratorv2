using System.ComponentModel.DataAnnotations;

namespace MonitoringConfigurator.Models
{
    public enum BuildingType
    {
        [Display(Name = "Dom jednorodzinny")] Home,
        [Display(Name = "Biuro / Sklep")] Office,
        [Display(Name = "Magazyn / Hala")] Warehouse,
        [Display(Name = "Parking / Plac")] Parking
    }

    public enum CameraEnvironment
    {
        [Display(Name = "Tylko wewnątrz")] Indoor,
        [Display(Name = "Tylko na zewnątrz")] Outdoor,
        [Display(Name = "Mieszane (Wew/Zew)")] Mixed
    }

    public enum InstallationType
    {
        [Display(Name = "Natynkowa (korytka)")] Surface,
        [Display(Name = "Podtynkowa (ukryta)")] Flush
    }

    // --- NOWE ENUMY ---
    public enum CameraTechnology
    {
        [Display(Name = "IP (PoE) - Nowoczesne")] IpPoe,
        [Display(Name = "Wi-Fi (Bezprzewodowe)")] Wifi,
        [Display(Name = "Analogowe (CVI/TVI/AHD)")] Analog
    }

    public enum DetectionType
    {
        [Display(Name = "Podstawowa (Ruch)")] Basic,
        [Display(Name = "Zaawansowana (AI - Ludzie/Pojazdy)")] Ai
    }

    public enum DisplayType
    {
        [Display(Name = "Tylko aplikacja (Brak ekranu)")] AppOnly,
        [Display(Name = "Monitor dedykowany")] Monitor,
        [Display(Name = "Telewizor (HDMI)")] Tv
    }

    public class ConfiguratorInputModel
    {
        // --- OBIEKT ---
        [Display(Name = "Rodzaj obiektu")]
        public BuildingType Building { get; set; }

        [Display(Name = "Metraż (m²)")]
        [Range(10, 100000)]
        public int AreaM2 { get; set; } = 150;

        [Display(Name = "Instalacja")]
        public InstallationType InstallType { get; set; }

        // --- KAMERY ---
        [Display(Name = "Technologia")]
        public CameraTechnology Tech { get; set; }

        [Display(Name = "Zewnętrzne (szt.)")]
        [Range(0, 128)]
        public int OutdoorCamCount { get; set; } = 4;

        [Display(Name = "Wewnętrzne (szt.)")]
        [Range(0, 128)]
        public int IndoorCamCount { get; set; } = 2;

        public int TotalCameras => OutdoorCamCount + IndoorCamCount;

        [Display(Name = "Rozdzielczość")]
        public int ResolutionMp { get; set; } = 4;

        [Display(Name = "Widoczność w nocy (IR)")]
        public int NightVisionM { get; set; } = 30; // np. 20, 30, 50, 80

        [Display(Name = "Rodzaj detekcji")]
        public DetectionType Detection { get; set; }

        // --- INNE ---
        [Display(Name = "Czas archiwizacji (dni)")]
        [Range(1, 90)]
        public int RecordingDays { get; set; } = 14;

        [Display(Name = "Wyświetlanie obrazu")]
        public DisplayType DisplayMethod { get; set; }

        [Display(Name = "Dodać okablowanie?")]
        public bool NeedCabling { get; set; } = true;

        [Display(Name = "Dodać UPS?")]
        public bool NeedUps { get; set; } = false;

        [Display(Name = "Czas podtrzymania (min)")]
        public int UpsRuntimeMinutes { get; set; } = 15;

        [Display(Name = "Usługa montażu")]
        public bool NeedAssembly { get; set; } = false;
    }

    public class ConfigurationResult
    {
        public ConfiguratorInputModel Input { get; set; }

        // Urządzenia
        public Product? SelectedOutdoorCam { get; set; }
        public Product? SelectedIndoorCam { get; set; }
        public Product? SelectedRecorder { get; set; } // NVR lub DVR
        public int RecorderQuantity { get; set; } = 1;
        public Product? SelectedSwitch { get; set; }
        public int SwitchQuantity { get; set; }
        public Product? SelectedDisk { get; set; }
        public int DiskQuantity { get; set; }
        public Product? SelectedMonitor { get; set; } // Monitor/TV
        public int MonitorQuantity { get; set; }

        // Akcesoria
        public Product? SelectedCable { get; set; }
        public int CableQuantity { get; set; }
        public Product? SelectedTray { get; set; }
        public int TrayMeters { get; set; }
        public Product? SelectedMount { get; set; } // Puszki/Uchwyty
        public int MountQuantity { get; set; }
        public Product? SelectedClips { get; set; }
        public int ClipsQuantity { get; set; }
        public Product? SelectedScrews { get; set; }
        public int ScrewsQuantity { get; set; }

        public Product? SelectedUps { get; set; }
        public int UpsQuantity { get; set; }

        // Usługa
        public decimal AssemblyCost { get; set; }

        // Statystyki
        public double EstimatedBandwidthMbps { get; set; }
        public double EstimatedStorageTB { get; set; }
        public int EstimatedPowerW { get; set; }
        public int EstimatedCableMeters { get; set; }

        public decimal TotalPrice =>
            ((SelectedOutdoorCam?.Price ?? 0) * Input.OutdoorCamCount) +
            ((SelectedIndoorCam?.Price ?? 0) * Input.IndoorCamCount) +
            ((SelectedRecorder?.Price ?? 0) * RecorderQuantity) +
            ((SelectedSwitch?.Price ?? 0) * SwitchQuantity) +
            ((SelectedDisk?.Price ?? 0) * DiskQuantity) +
            ((SelectedMonitor?.Price ?? 0) * MonitorQuantity) +
            ((SelectedCable?.Price ?? 0) * CableQuantity) +
            ((SelectedUps?.Price ?? 0) * UpsQuantity) +
            ((SelectedTray?.Price ?? 0) * TrayMeters) +
            ((SelectedMount?.Price ?? 0) * MountQuantity) +
            ((SelectedClips?.Price ?? 0) * ClipsQuantity) +
            ((SelectedScrews?.Price ?? 0) * ScrewsQuantity) +
            AssemblyCost;
    }
}