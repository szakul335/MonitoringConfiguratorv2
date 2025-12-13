using Microsoft.AspNetCore.Mvc;

namespace MonitoringConfigurator.Controllers
{
    public class HomeController : Controller
    {
        public IActionResult Index() => View();
        public IActionResult Privacy() => View();
    }
}
