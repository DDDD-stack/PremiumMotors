using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using WEBTechnologies_Final.Models;

namespace WEBTechnologies_Final.Controllers
{
    public class HomeController : Controller
    {
        // "/Home" was left over from the scaffold and rendered an empty
        // welcome page. The app root is the car listings.
        public IActionResult Index() => RedirectToAction("Index", "Cars");

        public IActionResult Privacy()
        {
            return View();
        }

        public IActionResult Terms()
        {
            return View();
        }

        /// <summary>
        /// Explains the private-offer model. First-time visitors arrive expecting either
        /// fixed-price classifieds or an auction and get neither, so leaving it unexplained
        /// makes the site feel broken rather than different.
        /// </summary>
        public IActionResult HowItWorks()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
