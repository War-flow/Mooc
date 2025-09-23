using Mooc.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Mooc.Components.Pages.Manager.CMS;

namespace Mooc.Services
{
    public partial class CourseStateService
    {

        /// <summary>
        /// Calcule le score total d'un cours
        /// </summary>
       
        /// <summary>
        /// Calcule les points totaux gagnés
        /// </summary>
        public async Task<int> CalculateTotalPointsAsync(int coursId, string? userId = null)
        {
            var scoreResult = await CalculateCourseScoreAsync(coursId, userId);
            return scoreResult.TotalEarnedPoints;
        }

        /// <summary>
        /// Calcule les points totaux possibles pour un cours
        /// </summary>
        public async Task<int> CalculateMaxPossiblePointsAsync(int coursId)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                var course = await context.Courses.FirstOrDefaultAsync(c => c.Id == coursId);
                
                if (course?.Content == null)
                    return 0;

                var blocks = JsonSerializer.Deserialize<List<CourBuilder.CoursBlock>>(
                    course.Content,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                ) ?? new List<CourBuilder.CoursBlock>();

                int totalPoints = 0;

                foreach (var block in blocks.Where(b => b.Type == "quiz"))
                {
                    try
                    {
                        var quizData = JsonSerializer.Deserialize<QuizStructure>(
                            block.Content?.ToString() ?? "{}",
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                        ) ?? new QuizStructure();

                        totalPoints += QuizScoring.DifficultyPoints[quizData.Difficulty];
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Erreur lors du calcul des points pour un bloc quiz: {ex.Message}");
                    }
                }

                return totalPoints;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur lors du calcul des points maximums possibles: {ex.Message}");
                return 0;
            }
        }
    }
}