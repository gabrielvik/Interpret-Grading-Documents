using Interpret_grading_documents.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;

namespace Interpret_grading_documents.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private static List<GPTService.GraduationDocument> _analyzedDocuments = new List<GPTService.GraduationDocument>();


        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        public IActionResult Index()
        {
            return View(_analyzedDocuments);
        }

        [HttpPost]
        public async Task<IActionResult> ProcessText(List<IFormFile> uploadedFiles)
        {
            if (uploadedFiles == null || uploadedFiles.Count == 0)
            {
                // Optionally, add a ModelState error or a TempData message to inform the user
                ViewBag.Error = "Please upload valid documents.";
                return View("Index");
            }

            foreach (var uploadedFile in uploadedFiles)
            {
                var extractedData = await GPTService.ProcessTextPrompts(uploadedFile);
                _analyzedDocuments.Add(extractedData);
            }

            return RedirectToAction("ViewUploadedDocuments");
        }


        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View();
        }
        public IActionResult ViewDocument(Guid id)
        {
            var document = _analyzedDocuments.Find(d => d.Id == id);
            if (document == null)
            {
                return NotFound();
            }
            return View(document);
        }
        public IActionResult ViewUploadedDocuments()
        {
            return View(_analyzedDocuments);
        }

        [HttpPost]
        public IActionResult RemoveDocument(Guid id)
        {
            var document = _analyzedDocuments.Find(d => d.Id == id);
            if (document != null)
            {
                _analyzedDocuments.Remove(document);

                Console.WriteLine($"Document {document.DocumentName} was successfully removed");
                Console.WriteLine($"\nDocuments remaining:");

                foreach (var doc in _analyzedDocuments)
                {
                    Console.WriteLine(doc.DocumentName);
                }

                return RedirectToAction("ViewUploadedDocuments");
            }
            return NotFound();
        }
    }
}
