using Interpret_grading_documents.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using Interpret_grading_documents.Data;

namespace Interpret_grading_documents.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private static List<GPTService.GraduationDocument> _analyzedDocuments = new List<GPTService.GraduationDocument>();

        private readonly string courseEquivalentsFilePath = Path.Combine(Directory.GetCurrentDirectory(), "CourseEquivalents.json");

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

        public IActionResult ViewUploadedDocuments()
        {
            return View(_analyzedDocuments);
        }

        [HttpPost]
        public IActionResult RemoveDocument(Guid id)
        {
            var document = _analyzedDocuments.FirstOrDefault(d => d.Id == id);
            if (document != null)
            {
                _analyzedDocuments.Remove(document);
                _logger.LogInformation($"Document {document.DocumentName} was successfully removed");
                return RedirectToAction("ViewUploadedDocuments");
            }
            return NotFound();
        }

        [HttpGet]
        public IActionResult CourseRequirementsManager()
        {
            var courseEquivalents = LoadCourseEquivalents();
            var validationCourses = ValidationData.GetCourses();

            var availableCourses = validationCourses.Values.Select(c => new AvailableCourse
            {
                CourseName = c.CourseName,
                CourseCode = c.CourseCode
            }).ToList();

            ViewBag.AvailableCourses = availableCourses;

            return View(courseEquivalents);
        }

        [HttpPost]
        public IActionResult SaveCourseEquivalents([FromBody] List<Course> courses)
        {
            var courseEquivalents = new CourseEquivalents { Subjects = new List<Subject>() };

            foreach (var subjectGroup in courses.GroupBy(c => c.Name))
            {
                var subject = new Subject
                {
                    Name = subjectGroup.Key,
                    Courses = subjectGroup.ToList()
                };
                courseEquivalents.Subjects.Add(subject);
            }

            SaveCourseEquivalentsToFile(courseEquivalents);
            return Json(new { success = true });
        }

        private CourseEquivalents LoadCourseEquivalents()
        {
            if (System.IO.File.Exists(courseEquivalentsFilePath))
            {
                var jsonContent = System.IO.File.ReadAllText(courseEquivalentsFilePath);
                return JsonSerializer.Deserialize<CourseEquivalents>(jsonContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            return new CourseEquivalents { Subjects = new List<Subject>() };
        }

        private void SaveCourseEquivalentsToFile(CourseEquivalents courseEquivalents)
        {
            var jsonContent = JsonSerializer.Serialize(courseEquivalents, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = null
            });
            System.IO.File.WriteAllText(courseEquivalentsFilePath, jsonContent);
        }

        public class AvailableCourse
        {
            public string CourseName { get; set; }
            public string CourseCode { get; set; }
        }
    }
}
