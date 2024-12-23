using Interpret_grading_documents.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using Interpret_grading_documents.Data;
using Interpret_grading_documents.Models;
using System.Reflection.Metadata;
using System.Security.Cryptography;

namespace Interpret_grading_documents.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IWebHostEnvironment _hostingEnvironment;

        private static Dictionary<string, List<GPTService.GraduationDocument>> _userDocuments = new Dictionary<string, List<GPTService.GraduationDocument>>();

        private readonly string courseEquivalentsFilePath = Path.Combine(Directory.GetCurrentDirectory(), "CourseEquivalents.json");
        private readonly string coursesForAverageFilePath = Path.Combine(Directory.GetCurrentDirectory(), "CoursesForAverage.json");

        public HomeController(ILogger<HomeController> logger, IWebHostEnvironment hostingEnvironment)
        {
            _logger = logger;
            _hostingEnvironment = hostingEnvironment;
        }

        private string GetUserSessionId()
        {
            if (!Request.Cookies.ContainsKey("UserSessionId"))
            {
                var sessionId = GenerateSessionId();
                Response.Cookies.Append("UserSessionId", sessionId, new CookieOptions { HttpOnly = true, IsEssential = true });
                return sessionId;
            }
            return Request.Cookies["UserSessionId"];
        }

        private string GenerateSessionId()
        {
            using (var rng = RandomNumberGenerator.Create())
            {
                var bytes = new byte[16];
                rng.GetBytes(bytes);
                return Convert.ToBase64String(bytes);
            }
        }

        private List<GPTService.GraduationDocument> GetUserDocuments()
        {
            var sessionId = GetUserSessionId();
            if (!_userDocuments.ContainsKey(sessionId))
            {
                _userDocuments[sessionId] = new List<GPTService.GraduationDocument>();
            }
            return _userDocuments[sessionId];
        }

        public IActionResult Index()
        {
            var coursesWithAverageFlag = GetCoursesWithAverageFlag();
            ViewBag.CoursesWithAverageFlag = coursesWithAverageFlag;

            var coursesForAverage = GetCoursesForAverage();
            ViewBag.CoursesForAverage = coursesForAverage;

            return View(GetUserDocuments());
        }

        [HttpGet]
        public async Task<IActionResult> ManageMeritCalculator()
        {
            var coursesForAverage = LoadCoursesForAverage() ?? new List<CourseForAverage>();

            var validationCourses = ValidationData.GetCombinedCourses();
            var availableCourses = validationCourses.Values.Select(c => new AvailableCourse
            {
                CourseName = c.CourseName,
                CourseCode = c.CourseCode
            }).ToList();

            ViewBag.AvailableCourses = availableCourses;

            var viewModel = new CoursesForAverageViewModel { CoursesForAverage = coursesForAverage };
            return View(viewModel);
        }

        [HttpPost]
        public IActionResult SaveCoursesForAverage([FromBody] List<CourseForAverage> coursesForAverage)
        {
            SaveCoursesForAverageToFile(coursesForAverage);
            return Json(new { success = true });
        }

        private List<CourseForAverage> LoadCoursesForAverage()
        {
            if (System.IO.File.Exists(coursesForAverageFilePath))
            {
                var jsonContent = System.IO.File.ReadAllText(coursesForAverageFilePath);
                return JsonSerializer.Deserialize<List<CourseForAverage>>(jsonContent);
            }
            return null;
        }

        private void SaveCoursesForAverageToFile(List<CourseForAverage> coursesForAverage)
        {
            var jsonContent = JsonSerializer.Serialize(coursesForAverage, new JsonSerializerOptions { WriteIndented = true });
            System.IO.File.WriteAllText(coursesForAverageFilePath, jsonContent);
        }

        private List<(string MainCourse, List<string> AlternativeCourses, bool IncludedInAverage)> GetCoursesWithAverageFlag()
        {
            var coursesWithAverageFlag = new List<(string MainCourse, List<string> AlternativeCourses, bool IncludedInAverage)>();
            var courseEquivalents = LoadCourseEquivalents();

            if (courseEquivalents != null)
            {
                foreach (var subject in courseEquivalents.Subjects)
                {
                    foreach (var course in subject.Courses)
                    {
                        var alternativeCourses = course.Alternatives.Select(alt => $"{alt.Name} ({alt.Code})").ToList();
                        coursesWithAverageFlag.Add(($"{course.Name} ({course.Code})", alternativeCourses, course.IncludeInAverage));
                    }
                }
            }
            return coursesWithAverageFlag;
        }

        private List<(string MainCourse, List<string> AlternativeCourses)> GetCoursesForAverage()
        {
            var coursesWithAverageFlag = new List<(string MainCourse, List<string> AlternativeCourses)>();
            var courseForAverageViewModel = LoadCourseForAverage();

            if (courseForAverageViewModel?.CoursesForAverage != null)
            {
                foreach (var course in courseForAverageViewModel.CoursesForAverage)
                {
                    var alternativeCourses = course.AlternativeCourses.Select(alt => $"{alt.Name} ({alt.Code})").ToList();
                    coursesWithAverageFlag.Add(($"{course.Name} ({course.Code})", alternativeCourses));
                }
            }
            return coursesWithAverageFlag;
        }

        [HttpPost]
        public async Task<IActionResult> ProcessText(List<IFormFile> uploadedFiles)
        {
            var userDocuments = GetUserDocuments();

            if (uploadedFiles == null || uploadedFiles.Count == 0)
            {
                ViewBag.Error = "Please upload valid documents.";
                return View("Index", userDocuments);
            }
            string existingPersonalId = userDocuments.FirstOrDefault()?.PersonalId;
            List<GPTService.GraduationDocument> newDocuments = new List<GPTService.GraduationDocument>();

            foreach (var uploadedFile in uploadedFiles)
            {
                var extractedData = await GPTService.ProcessTextPrompts(uploadedFile);

                // Save the uploaded file to a permanent location
                var uploadsFolder = Path.Combine(_hostingEnvironment.WebRootPath, "uploads");
                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                }
                var uniqueFileName = $"{Guid.NewGuid()}{Path.GetExtension(uploadedFile.FileName).ToLower()}";
                var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await uploadedFile.CopyToAsync(stream);
                }

                extractedData.FilePath = filePath;
                extractedData.ContentType = uploadedFile.ContentType;

                // Check if the ImageReliability score is 0
                if (extractedData.ImageReliability.ReliabilityScore == 0)
                {
                    ViewBag.Error = $"The uploaded document {extractedData.DocumentName} has too low a reliability score and cannot be analyzed.";
                    return View("Index", userDocuments);
                }

                if (string.IsNullOrEmpty(existingPersonalId))
                {
                    existingPersonalId = extractedData.PersonalId;
                }
                else if (extractedData.PersonalId != existingPersonalId)
                {
                    ViewBag.Error = "One or more uploaded documents do not match the social security ID of previously uploaded documents.";
                    return View("Index", userDocuments);
                }

                newDocuments.Add(extractedData);
            }

            userDocuments.AddRange(newDocuments);

            return RedirectToAction("ViewUploadedDocuments");
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View();
        }

        public IActionResult ViewDocument(Guid id)
        {
            var document = GetUserDocuments().Find(d => d.Id == id);
            if (document == null)
            {
                return NotFound();
            }
            return View(document);
        }

        public async Task<IActionResult> ViewUploadedDocuments()
        {
            var userDocuments = GetUserDocuments();

            if (userDocuments.Count == 0)
            {
                ViewBag.UserName = null;
                ViewBag.ExamStatus = null;
            }
            else
            {
                string highestExamStatus = GPTService.GetHighestExamStatus(userDocuments);
                string userName = userDocuments.FirstOrDefault()?.FullName ?? "Uploaded";

                ViewBag.UserName = userName;
                ViewBag.ExamStatus = highestExamStatus;
            }

            string jsonFilePath = Path.Combine(_hostingEnvironment.ContentRootPath, "CourseEquivalents.json");
            ViewBag.JsonFilePath = jsonFilePath;

            string jsonFilePathForAverage = Path.Combine(_hostingEnvironment.ContentRootPath, "CoursesForAverage.json");
            ViewBag.JsonFilePathForAverage = jsonFilePathForAverage;

            var mergedDocument = MergeDocuments(userDocuments);

            var averageMeritPoints = await CalculateAverageMeritPoints(mergedDocument);
            ViewBag.AverageMeritPoints = averageMeritPoints;

            var viewModel = new UploadedDocumentsViewModel
            {
                Documents = userDocuments,
                MergedDocument = mergedDocument
            };

            return View(viewModel);
        }

        private GPTService.GraduationDocument MergeDocuments(List<GPTService.GraduationDocument> documents)
        {
            var mergedDocument = new GPTService.GraduationDocument
            {
                Id = Guid.NewGuid(),
                FullName = documents.FirstOrDefault()?.FullName,
                PersonalId = documents.FirstOrDefault()?.PersonalId,
                HasValidDegree = documents.FirstOrDefault()?.HasValidDegree,
                DocumentName = "Merged Document"
            };

            var subjectsDict = new Dictionary<string, GPTService.Subject>(StringComparer.OrdinalIgnoreCase);

            foreach (var doc in documents)
            {
                foreach (var subject in doc.Subjects)
                {
                    string key = subject.SubjectName.Trim().ToLower();

                    if (subjectsDict.TryGetValue(key, out var existingSubject))
                    {
                        double existingGradeValue = RequirementChecker.GetGradeValue(existingSubject.Grade);
                        double newGradeValue = RequirementChecker.GetGradeValue(subject.Grade);

                        if (newGradeValue > existingGradeValue)
                        {
                            subjectsDict[key] = subject;
                        }
                    }
                    else
                    {
                        subjectsDict[key] = subject;
                    }
                }
            }

            mergedDocument.Subjects = subjectsDict.Values.ToList();

            return mergedDocument;
        }

        private async Task<double> CalculateAverageMeritPoints(GPTService.GraduationDocument document)
        {
            var coursesForAverage = LoadCoursesForAverage();
            if (coursesForAverage == null) return 0;

            // Fetch combined courses from ValidationData for additional course details
            var validationCourses = ValidationData.GetCombinedCourses();

            // Build a dictionary from course codes to CourseDetail
            var courseCodeToCourseDetail = validationCourses.Values
                .Where(c => !string.IsNullOrEmpty(c.CourseCode))
                .ToDictionary(c => c.CourseCode.Trim(), c => c, StringComparer.OrdinalIgnoreCase);

            double totalWeightedGradePoints = 0;
            int totalCoursePoints = 0;

            foreach (var courseForAverage in coursesForAverage)
            {
                // Create a list of equivalent courses (main and alternatives)
                var equivalentCourses = new List<string> { courseForAverage.Code };
                equivalentCourses.AddRange(courseForAverage.AlternativeCourses.Select(alt => alt.Code));

                // Check if the course or an equivalent is in the document
                var matchingSubject = document.Subjects
                    .FirstOrDefault(s => equivalentCourses.Contains(s.CourseCode?.Trim(), StringComparer.OrdinalIgnoreCase));

                int points = 0;
                double gradeValue = 0;

                if (matchingSubject != null)
                {
                    // Course is in the document
                    gradeValue = RequirementChecker.GetGradeValue(matchingSubject.Grade.Trim());
                    points = int.TryParse(matchingSubject.GymnasiumPoints, out var parsedPoints) ? parsedPoints : 0;
                }
                else
                {
                    // Course not in document, get points from validation data
                    var foundCourse = equivalentCourses
                        .Select(code => courseCodeToCourseDetail.TryGetValue(code.Trim(), out var c) ? c : null)
                        .FirstOrDefault(c => c != null);

                    if (foundCourse != null)
                    {
                        gradeValue = 0; // Assign a grade value of 0 for missing courses
                        points = foundCourse.Points ?? 0;
                    }
                    else
                    {
                        // Course code not found in validation data, skip to next course
                        continue;
                    }
                }

                totalWeightedGradePoints += gradeValue * points;
                totalCoursePoints += points;
            }

            // Calculate the average if totalCoursePoints is greater than zero
            return totalCoursePoints > 0 ? Math.Round(totalWeightedGradePoints / totalCoursePoints, 2) : 0;
        }




        [HttpPost]
        public IActionResult RemoveDocument(Guid id)
        {
            var userDocuments = GetUserDocuments();
            var document = userDocuments.Find(d => d.Id == id);
            if (document != null)
            {
                userDocuments.Remove(document);
                return RedirectToAction("ViewUploadedDocuments");
            }
            return NotFound();
        }

        [HttpGet]
        public async Task<IActionResult> CourseRequirementsManager()
        {
            var courseEquivalents = LoadCourseEquivalents() ?? new CourseEquivalents
            {
                Subjects = new List<Subject>()
            };

            var validationCourses = ValidationData.GetCombinedCourses();

            var availableCourses = validationCourses.Values.Select(c => new AvailableCourse
            {
                CourseName = c.CourseName,
                CourseCode = c.CourseCode
            }).ToList();

            ViewBag.AvailableCourses = availableCourses;

            return View(courseEquivalents);
        }

        [HttpPost]
        public IActionResult SaveCourseEquivalents([FromBody] CourseEquivalents courseEquivalents)
        {
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
            return null;
        }

        private CoursesForAverageViewModel LoadCourseForAverage()
        {
            if (System.IO.File.Exists(coursesForAverageFilePath))
            {
                var jsonContent = System.IO.File.ReadAllText(coursesForAverageFilePath);
                var courses = JsonSerializer.Deserialize<List<CourseForAverage>>(jsonContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return new CoursesForAverageViewModel { CoursesForAverage = courses };
            }
            return null;
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

        [HttpGet]
        public IActionResult CheckRequirements(Guid id)
        {
            string jsonFilePath = Path.Combine(_hostingEnvironment.ContentRootPath, "CourseEquivalents.json");
            var document = GetUserDocuments().Find(d => d.Id == id);
            if (document == null)
            {
                return NotFound();
            }

            var requirementResults = RequirementChecker.DoesStudentMeetRequirement(document, jsonFilePath);

            var averageMeritPoints = CalculateAverageMeritPoints(document);

            bool meetsAllRequirements = requirementResults.Values.All(r => r.IsMet);

            var model = new RequirementCheckViewModel
            {
                Document = document,
                RequirementResults = requirementResults,
                MeetsRequirement = meetsAllRequirements
            };

            ViewBag.AverageMeritPoints = averageMeritPoints;

            return View(model);
        }

        [HttpGet]
        public IActionResult GetDocumentFile(Guid id)
        {
            var document = GetUserDocuments().FirstOrDefault(d => d.Id == id);
            if (document == null || string.IsNullOrEmpty(document.FilePath))
            {
                return NotFound("Document not found or file unavailable.");
            }

            var fileBytes = System.IO.File.ReadAllBytes(document.FilePath);

            if (document.ContentType == "application/pdf")
            {
                Response.Headers.Add("Content-Disposition", "inline");
            }

            return File(fileBytes, document.ContentType);
        }

        [HttpPost]
        public IActionResult SaveDocument(GPTService.GraduationDocument updatedDocument)
        {
            if (updatedDocument == null)
            {
                return BadRequest();
            }

            var userDocuments = GetUserDocuments();
            var existingDocument = userDocuments.FirstOrDefault(d => d.Id == updatedDocument.Id);
            if (existingDocument == null)
            {
                return NotFound();
            }

            // Update the fields
            existingDocument.FullName = updatedDocument.FullName;
            existingDocument.PersonalId = updatedDocument.PersonalId;

            // Update the subjects
            if (updatedDocument.Subjects != null && updatedDocument.Subjects.Count > 0)
            {
                foreach (var subject in updatedDocument.Subjects)
                {
                    // Set FuzzyMatchScore to 100 to indicate confirmed matches
                    subject.FuzzyMatchScore = 100.0;

                    // Clear original values as they are no longer needed
                    subject.OriginalSubjectName = null;
                    subject.OriginalCourseCode = null;
                    subject.OriginalGymnasiumPoints = null;
                }
                existingDocument.Subjects = updatedDocument.Subjects;
            }

            // Save changes to the data store if applicable

            return RedirectToAction("ViewDocument", new { id = updatedDocument.Id }); // Replace with your desired action
        }

        public bool UserHasValidExam()
        {
            var userDocuments = GetUserDocuments();
            foreach (var document in userDocuments)
            {
                GPTService.ExamValidator(document);

                if (!string.IsNullOrEmpty(document.HasValidDegree) && document.HasValidDegree.Contains("examen", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
