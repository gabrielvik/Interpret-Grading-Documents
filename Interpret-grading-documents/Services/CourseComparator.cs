using System.Text.Json;
using System.Linq;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace Interpret_grading_documents.Services;

public class CourseDetail
{
    public string Code { get; set; }
    public string Name { get; set; }
    public string Points { get; set; }
}

public class ApiResponse
{
    public CourseDetail Course { get; set; }
}

public class CourseComparator
{
    private GPTService.GraduationDocument graduationDocument;

    public CourseComparator(GPTService.GraduationDocument graduationDocument)
    {
        this.graduationDocument = graduationDocument;
    }

    private async Task<CourseDetail> FetchCourseDataFromApi(string courseCode)
    {
        string url = $"https://api.skolverket.se/syllabus/v1/courses/{courseCode}";

        using (HttpClient client = new HttpClient())
        {
            try
            {
                HttpResponseMessage response = await client.GetAsync(url);
                string apiResponse = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    // The response contains the entire API response, including metadata
                    var apiResponseObject = JsonSerializer.Deserialize<ApiResponse>(apiResponse, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (apiResponseObject != null && apiResponseObject.Course != null)
                    {
                        return apiResponseObject.Course;
                    }
                    else
                    {
                        Console.WriteLine($"No course data found in API response for course code {courseCode}.");
                        return null;
                    }
                }
                else
                {
                    Console.WriteLine($"Failed to fetch data from API for course code {courseCode}. Status code: {response.StatusCode}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception occurred while fetching data from API for course code {courseCode}: {ex.Message}");
                return null;
            }
        }
    }

    public async Task<GPTService.GraduationDocument> UpdateMatchedSubjectsAsync()
    {
        foreach (var subject in graduationDocument.Subjects)
        {
            // Attempt to fetch course using the provided course code
            var courseDetail = await FetchCourseDataFromApi(subject.CourseCode);

            if (courseDetail != null)
            {
                // Update subject details
                subject.SubjectName = courseDetail.Name;
                subject.GymnasiumPoints = courseDetail.Points;
                subject.CourseCode = courseDetail.Code;
                subject.FuzzyMatchScore = 100; // Exact match
            }
            else
            {
                // Try to adjust the course code and try again or use fuzzy matching

            }
        }

        return graduationDocument;
    }

    public async Task<GPTService.GraduationDocument> CompareCoursesAsync()
    {
        var updatedDocument = await UpdateMatchedSubjectsAsync();

        // Calculate total points
        int totalPoints = updatedDocument.Subjects.Sum(s => int.TryParse(s.GymnasiumPoints, out int points) ? points : 0);

        Console.WriteLine($"Total Points of Matched Courses: {totalPoints}");
        Console.WriteLine("Subjects that did not match any courses:");

        foreach (var subject in updatedDocument.Subjects.Where(s => s.FuzzyMatchScore == 0))
        {
            Console.WriteLine(subject.SubjectName);
        }

        return updatedDocument;
    }
}
