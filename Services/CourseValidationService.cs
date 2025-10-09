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
        public int QuestionnaireCount { get; set; }
        public int TotalQuestions { get; set; }
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
                result.AddError("Le cours doit contenir au moins un bloc questionnaire");
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
                    result.AddError("Le cours doit contenir au moins un bloc questionnaire");
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
                result.AddError("Le cours doit contenir au moins un bloc questionnaire");
                return result;
            }

            // Compter les blocs questionnaire
            var questionnaireBlocks = blocks.Where(b => b.Type == "questionnaire").ToList();
            result.QuestionnaireCount = questionnaireBlocks.Count;

            // **VALIDATION PRINCIPALE : Au moins un questionnaire obligatoire**
            if (result.QuestionnaireCount == 0)
            {
                result.AddError("Le cours doit contenir au moins un bloc questionnaire");
            }
            else if (result.QuestionnaireCount > 1)
            {
                result.AddWarning($"Le cours contient {result.QuestionnaireCount} questionnaires. Un seul questionnaire par cours est recommandé.");
            }

            // Valider chaque bloc questionnaire
            foreach (var questionnaireBlock in questionnaireBlocks)
            {
                ValidateQuestionnaireBlock(questionnaireBlock, result);
            }

            return result;
        }

        private void ValidateQuestionnaireBlock(Mooc.Components.Pages.Manager.CMS.CourBuilder.CoursBlock questionnaireBlock, CourseValidationResult result)
        {
            try
            {
                if (questionnaireBlock.Content == null)
                {
                    result.AddError($"Le questionnaire '{questionnaireBlock.Title}' n'a pas de contenu défini");
                    return;
                }

                var questionnaireContent = JsonSerializer.Deserialize<QuestionnaireContentModel>(
                    questionnaireBlock.Content.ToString()!,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (questionnaireContent == null)
                {
                    result.AddError($"Le questionnaire '{questionnaireBlock.Title}' a un contenu invalide");
                    return;
                }

                // Valider le titre du questionnaire (optionnel mais recommandé)
                if (string.IsNullOrWhiteSpace(questionnaireContent.Title))
                {
                    result.AddWarning($"Le questionnaire '{questionnaireBlock.Title}' n'a pas de titre défini");
                }

                // Valider les questions
                if (questionnaireContent.Questions == null || !questionnaireContent.Questions.Any())
                {
                    result.AddError($"Le questionnaire '{questionnaireBlock.Title}' doit contenir au moins une question");
                    return;
                }

                // Compter le nombre total de questions
                result.TotalQuestions += questionnaireContent.Questions.Count;

                // Valider chaque question
                for (int i = 0; i < questionnaireContent.Questions.Count; i++)
                {
                    var question = questionnaireContent.Questions[i];
                    var questionNumber = i + 1;

                    // Valider le texte de la question
                    if (string.IsNullOrWhiteSpace(question.Question))
                    {
                        result.AddError($"Le questionnaire '{questionnaireBlock.Title}' - Question {questionNumber} : Le texte de la question est obligatoire");
                    }

                    // Valider les options
                    if (question.Options == null || question.Options.Count < 2)
                    {
                        result.AddError($"Le questionnaire '{questionnaireBlock.Title}' - Question {questionNumber} : Au moins 2 options de réponse sont requises");
                    }
                    else
                    {
                        // Vérifier qu'il y a au moins une bonne réponse
                        if (!question.Options.Any(o => o.IsCorrect))
                        {
                            result.AddError($"Le questionnaire '{questionnaireBlock.Title}' - Question {questionNumber} : Au moins une réponse correcte est requise");
                        }

                        // Vérifier que toutes les options ont du texte
                        var emptyOptions = question.Options.Where(o => string.IsNullOrWhiteSpace(o.Text)).Count();
                        if (emptyOptions > 0)
                        {
                            result.AddError($"Le questionnaire '{questionnaireBlock.Title}' - Question {questionNumber} : {emptyOptions} option(s) sans texte");
                        }

                        // Validation spécifique par type de question
                        switch (question.Type)
                        {
                            case "multiple-choice":
                                // Pour choix unique, vérifier qu'il n'y a qu'une seule bonne réponse
                                var correctCount = question.Options.Count(o => o.IsCorrect);
                                if (correctCount > 1)
                                {
                                    result.AddWarning($"Le questionnaire '{questionnaireBlock.Title}' - Question {questionNumber} : Une seule réponse correcte attendue pour un choix unique (trouvé: {correctCount})");
                                }
                                break;

                            case "true-false":
                                // Pour vrai/faux, vérifier qu'il y a exactement 2 options
                                if (question.Options.Count != 2)
                                {
                                    result.AddError($"Le questionnaire '{questionnaireBlock.Title}' - Question {questionNumber} : Une question Vrai/Faux doit avoir exactement 2 options");
                                }
                                break;

                            case "multiple-select":
                                // Pour choix multiple, au moins une bonne réponse (déjà vérifié)
                                break;

                            default:
                                result.AddWarning($"Le questionnaire '{questionnaireBlock.Title}' - Question {questionNumber} : Type de question non reconnu '{question.Type}'");
                                break;
                        }
                    }
                }

                // Recommandations
                if (questionnaireContent.Questions.Count < 3)
                {
                    result.AddWarning($"Le questionnaire '{questionnaireBlock.Title}' contient seulement {questionnaireContent.Questions.Count} question(s). Au moins 3 questions sont recommandées.");
                }
                else if (questionnaireContent.Questions.Count > 20)
                {
                    result.AddWarning($"Le questionnaire '{questionnaireBlock.Title}' contient {questionnaireContent.Questions.Count} questions. Un questionnaire trop long peut décourager les apprenants.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la validation du questionnaire {QuestionnaireTitle}", questionnaireBlock.Title);
                result.AddError($"Erreur lors de la validation du questionnaire '{questionnaireBlock.Title}'");
            }
        }
    }
}