using Microsoft.EntityFrameworkCore;
using Mooc.Data;
using System.Text.Json;

namespace Mooc.Services
{
    public interface ICourseValidationService
    {
        Task<CourseValidationResult> ValidateCourseAsync(int coursId);
        Task<CourseValidationResult> ValidateCourseContentAsync(string? content);
        CourseValidationResult ValidateCoursBlocks(List<Mooc.Components.Pages.Manager.CMS.CourBuilder.CoursBlock> blocks);
    }

    public class CourseValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public int QuizCount { get; set; }
        public int TotalBlocks { get; set; }

        public void AddError(string error)
        {
            Errors.Add(error);
            IsValid = false;
        }

        public void AddWarning(string warning)
        {
            Warnings.Add(warning);
        }
    }

    public class CourseValidationService : ICourseValidationService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private readonly ILogger<CourseValidationService> _logger;

        public CourseValidationService(
            IDbContextFactory<ApplicationDbContext> contextFactory,
            ILogger<CourseValidationService> logger)
        {
            _contextFactory = contextFactory;
            _logger = logger;
        }

        public async Task<CourseValidationResult> ValidateCourseAsync(int coursId)
        {
            var result = new CourseValidationResult();

            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                var cours = await context.Courses.FindAsync(coursId);

                if (cours == null)
                {
                    result.AddError("Cours introuvable");
                    return result;
                }

                // Valider le contenu JSON
                return await ValidateCourseContentAsync(cours.Content);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la validation du cours {CoursId}", coursId);
                result.AddError("Erreur lors de la validation du cours");
                return result;
            }
        }

        public async Task<CourseValidationResult> ValidateCourseContentAsync(string? content)
        {
            var result = new CourseValidationResult();

            if (string.IsNullOrEmpty(content))
            {
                result.AddError("Le cours doit contenir au moins un quiz");
                return result;
            }

            try
            {
                var blocks = JsonSerializer.Deserialize<List<Mooc.Components.Pages.Manager.CMS.CourBuilder.CoursBlock>>(
                    content,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                if (blocks == null || !blocks.Any())
                {
                    result.AddError("Le cours doit contenir au moins un quiz");
                    return result;
                }

                return ValidateCoursBlocks(blocks);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Erreur de désérialisation du contenu du cours");
                result.AddError("Format du contenu du cours invalide");
                return result;
            }
        }

        public CourseValidationResult ValidateCoursBlocks(List<Mooc.Components.Pages.Manager.CMS.CourBuilder.CoursBlock> blocks)
        {
            var result = new CourseValidationResult
            {
                TotalBlocks = blocks.Count,
                IsValid = true
            };

            if (!blocks.Any())
            {
                result.AddError("Le cours doit contenir au moins un quiz");
                return result;
            }

            // Compter les quiz
            var quizBlocks = blocks.Where(b => b.Type == "quiz").ToList();
            result.QuizCount = quizBlocks.Count;

            // **VALIDATION PRINCIPALE : Au moins un quiz obligatoire**
            if (result.QuizCount == 0)
            {
                result.AddError("Le cours doit contenir au moins un quiz");
            }

            // Valider chaque bloc quiz
            foreach (var quizBlock in quizBlocks)
            {
                ValidateQuizBlock(quizBlock, result);
            }

            // Avertissements additionnels
            if (result.QuizCount == 1)
            {
                result.AddWarning("Il est recommandé d'avoir plusieurs quiz pour une meilleure évaluation");
            }

            if (blocks.Count(b => b.Type == "texte") == 0)
            {
                result.AddWarning("Il est recommandé d'avoir du contenu textuel en plus des quiz");
            }

            return result;
        }

        private void ValidateQuizBlock(Mooc.Components.Pages.Manager.CMS.CourBuilder.CoursBlock quizBlock, CourseValidationResult result)
        {
            try
            {
                if (quizBlock.Content == null)
                {
                    result.AddError($"Le quiz '{quizBlock.Title}' n'a pas de contenu défini");
                    return;
                }

                var quizContent = JsonSerializer.Deserialize<QuizStructure>(
                    quizBlock.Content.ToString()!,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (quizContent == null)
                {
                    result.AddError($"Le quiz '{quizBlock.Title}' a un contenu invalide");
                    return;
                }

                // Valider la question
                if (string.IsNullOrWhiteSpace(quizContent.Question))
                {
                    result.AddError($"Le quiz '{quizBlock.Title}' doit avoir une question");
                }

                // Valider les options
                if (quizContent.Options == null || quizContent.Options.Count < 2)
                {
                    result.AddError($"Le quiz '{quizBlock.Title}' doit avoir au moins 2 options de réponse");
                }
                else
                {
                    // Vérifier qu'il y a au moins une bonne réponse
                    if (!quizContent.Options.Any(o => o.IsCorrect))
                    {
                        result.AddError($"Le quiz '{quizBlock.Title}' doit avoir au moins une réponse correcte");
                    }

                    // Vérifier que toutes les options ont du texte
                    var emptyOptions = quizContent.Options.Where(o => string.IsNullOrWhiteSpace(o.Text)).Count();
                    if (emptyOptions > 0)
                    {
                        result.AddError($"Le quiz '{quizBlock.Title}' a {emptyOptions} option(s) sans texte");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la validation du quiz {QuizTitle}", quizBlock.Title);
                result.AddError($"Erreur lors de la validation du quiz '{quizBlock.Title}'");
            }
        }
    }
}