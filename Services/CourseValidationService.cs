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
        public bool IsValid { get; set; } = true;
        public List<ValidationMessage> Errors { get; set; } = new();
        public List<ValidationMessage> Warnings { get; set; } = new();
        public int QuestionnaireCount { get; set; }
        public int TotalQuestions { get; set; }
        public int TotalBlocks { get; set; }

        public void AddError(string error, string? context = null, string? suggestion = null)
        {
            Errors.Add(new ValidationMessage 
            { 
                Message = error, 
                Context = context,
                Suggestion = suggestion,
                Severity = ValidationSeverity.Error 
            });
            IsValid = false;
        }

        public void AddWarning(string warning, string? context = null, string? suggestion = null)
        {
            Warnings.Add(new ValidationMessage 
            { 
                Message = warning, 
                Context = context,
                Suggestion = suggestion,
                Severity = ValidationSeverity.Warning 
            });
        }

        /// <summary>
        /// Retourne tous les messages d'erreur sous forme de chaînes simples (compatibilité)
        /// </summary>
        public List<string> GetErrorMessages() => Errors.Select(e => e.ToString()).ToList();

        /// <summary>
        /// Retourne tous les messages d'avertissement sous forme de chaînes simples (compatibilité)
        /// </summary>
        public List<string> GetWarningMessages() => Warnings.Select(w => w.ToString()).ToList();
    }

    public class ValidationMessage
    {
        public required string Message { get; set; }
        public string? Context { get; set; }
        public string? Suggestion { get; set; }
        public ValidationSeverity Severity { get; set; }

        public override string ToString()
        {
            var result = Message;
            if (!string.IsNullOrEmpty(Context))
                result += $" ({Context})";
            if (!string.IsNullOrEmpty(Suggestion))
                result += $" - {Suggestion}";
            return result;
        }
    }

    public enum ValidationSeverity
    {
        Error,
        Warning,
        Info
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
                    result.AddError(
                        $"Le cours avec l'identifiant {coursId} est introuvable",
                        "Validation du cours",
                        "Vérifiez que le cours existe dans la base de données"
                    );
                    return result;
                }

                // Valider le contenu JSON
                return await ValidateCourseContentAsync(cours.Content);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la validation du cours {CoursId}", coursId);
                result.AddError(
                    "Une erreur technique s'est produite lors de la validation",
                    $"Cours ID: {coursId}",
                    "Consultez les logs pour plus de détails"
                );
                return result;
            }
        }

        public async Task<CourseValidationResult> ValidateCourseContentAsync(string? content)
        {
            var result = new CourseValidationResult();

            if (string.IsNullOrEmpty(content))
            {
                result.AddError(
                    "Le cours ne contient aucun contenu",
                    "Contenu du cours vide",
                    "Ajoutez au moins un bloc de contenu (cours) pour créer un cours valide"
                );
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
                    result.AddError(
                        "Le cours doit contenir au moins un bloc de contenu",
                        "Structure du cours invalide",
                        "Créez un bloc de cours pour commencer à structurer votre contenu"
                    );
                    return result;
                }

                return ValidateCoursBlocks(blocks);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Erreur de désérialisation du contenu du cours");
                result.AddError(
                    "Le format du contenu du cours est invalide",
                    "Erreur de désérialisation JSON",
                    "Le contenu du cours a été corrompu. Essayez de recréer les blocs de contenu"
                );
                return result;
            }
        }

        public CourseValidationResult ValidateCoursBlocks(List<Mooc.Components.Pages.Manager.CMS.CourBuilder.CoursBlock> blocks)
        {
            var result = new CourseValidationResult();

            if (blocks == null || !blocks.Any())
            {
                result.AddError(
                    "Aucun bloc de contenu détecté",
                    "Liste de blocs vide",
                    "Ajoutez au moins un bloc de type 'cours' pour commencer"
                );
                return result;
            }

            result.TotalBlocks = blocks.Count;

            // ✅ VALIDATION STRICTE : Vérifier qu'il y a exactement un bloc de type "texte"
            var coursBlocks = blocks.Where(b => b.Type == "texte").ToList();
            if (!coursBlocks.Any())
            {
                result.AddError(
                    "Le cours doit contenir au moins un bloc de contenu pédagogique",
                    "Aucun bloc de type 'cours' trouvé",
                    "Cliquez sur 'Ajouter un cours' pour créer le contenu principal de votre cours"
                );
            }
            else if (coursBlocks.Count > 1)
            {
                result.AddWarning(
                    $"Le cours contient {coursBlocks.Count} blocs de cours",
                    "Plusieurs blocs de cours détectés",
                    "Pour une meilleure organisation, il est recommandé d'avoir un seul bloc de cours principal"
                );
            }
            else
            {
                // ✅ NOUVEAU : Vérifier que le bloc cours n'est pas vide
                var coursBlock = coursBlocks.First();
                bool isCoursEmpty = string.IsNullOrWhiteSpace(coursBlock.Title) && 
                                   string.IsNullOrWhiteSpace(coursBlock.Text);
                
                if (isCoursEmpty)
                {
                    result.AddError(
                        "Le bloc de cours est vide",
                        "Contenu du cours manquant",
                        "Remplissez le titre et/ou le contenu du bloc de cours avant d'enregistrer"
                    );
                }
            }

            // ✅ VALIDATION STRICTE : Vérifier les questionnaires
            var questionnaireBlocks = blocks.Where(b => b.Type == "questionnaire" || b.Type == "quiz").ToList();
            result.QuestionnaireCount = questionnaireBlocks.Count;

            // ✅ NOUVEAU : Exiger au moins un questionnaire
            if (questionnaireBlocks.Count == 0)
            {
                result.AddError(
                    "Le cours doit contenir un questionnaire d'évaluation",
                    "Aucun questionnaire trouvé",
                    "Ajoutez un questionnaire pour permettre aux apprenants de valider leurs connaissances"
                );
            }
            else if (questionnaireBlocks.Count > 1)
            {
                result.AddError(
                    $"Le cours contient {questionnaireBlocks.Count} questionnaires",
                    "Limite de questionnaires dépassée",
                    "Supprimez les questionnaires en trop pour n'en garder qu'un seul"
                );
            }

            // Valider chaque questionnaire
            int questionnaireIndex = 1;
            foreach (var qBlock in questionnaireBlocks)
            {
                string questionnaireContext = questionnaireBlocks.Count > 1 
                    ? $"Questionnaire #{questionnaireIndex}" 
                    : "Questionnaire";

                QuestionnaireContentModel? questionnaire = null;

                // ✅ CORRECTION : Gérer plusieurs types de Content
                try
                {
                    if (qBlock.Content is JsonElement jsonElement)
                    {
                        // Cas 1 : Content est un JsonElement
                        questionnaire = JsonSerializer.Deserialize<QuestionnaireContentModel>(
                            jsonElement.GetRawText(),
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                        );
                    }
                    else if (qBlock.Content is QuestionnaireContentModel directModel)
                    {
                        // Cas 2 : Content est déjà un QuestionnaireContentModel
                        questionnaire = directModel;
                    }
                    else if (qBlock.Content != null)
                    {
                        // Cas 3 : Tenter de désérialiser depuis string
                        var jsonString = qBlock.Content.ToString();
                        if (!string.IsNullOrEmpty(jsonString))
                        {
                            questionnaire = JsonSerializer.Deserialize<QuestionnaireContentModel>(
                                jsonString,
                                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                            );
                        }
                    }

                    // ✅ Valider le questionnaire
                    if (questionnaire == null || questionnaire.Questions == null || !questionnaire.Questions.Any())
                    {
                        result.AddError(
                            "Le questionnaire ne contient aucune question",
                            questionnaireContext,
                            "Ajoutez au moins une question avec plusieurs choix de réponses"
                        );
                    }
                    else
                    {
                        result.TotalQuestions += questionnaire.Questions.Count;

                        // Valider chaque question
                        for (int i = 0; i < questionnaire.Questions.Count; i++)
                        {
                            var question = questionnaire.Questions[i];
                            string questionContext = $"{questionnaireContext} - Question #{i + 1}";

                            if (string.IsNullOrWhiteSpace(question.Question))
                            {
                                result.AddError(
                                    "La question n'a pas de texte",
                                    questionContext,
                                    "Saisissez le texte de la question avant de continuer"
                                );
                            }
                            else
                            {
                                questionContext += $" ({question.Question.Substring(0, Math.Min(30, question.Question.Length))}...)";
                            }

                            if (question.Options == null || question.Options.Count < 2)
                            {
                                result.AddError(
                                    "La question doit avoir au moins 2 choix de réponses",
                                    questionContext,
                                    $"Ajoutez {(question.Options?.Count == 0 ? "au moins 2" : "encore " + (2 - (question.Options?.Count ?? 0)))} choix de réponse"
                                );
                            }
                            else if (question.Options.Count < 3)
                            {
                                result.AddWarning(
                                    $"La question n'a que {question.Options.Count} choix",
                                    questionContext,
                                    "Il est recommandé d'avoir au moins 3 choix pour une meilleure évaluation"
                                );
                            }

                            if (question.Options != null)
                            {
                                var correctAnswers = question.Options.Where(c => c.IsCorrect).ToList();
                                
                                if (!correctAnswers.Any())
                                {
                                    result.AddError(
                                        "Aucune réponse correcte n'est définie",
                                        questionContext,
                                        "Cochez au moins une option comme étant la réponse correcte"
                                    );
                                }
                                else if (correctAnswers.Count == question.Options.Count)
                                {
                                    result.AddWarning(
                                        "Toutes les réponses sont marquées comme correctes",
                                        questionContext,
                                        "Cela rend la question invalide. Décochez les mauvaises réponses"
                                    );
                                }

                                // Vérifier les options vides
                                var emptyOptions = question.Options.Where(o => string.IsNullOrWhiteSpace(o.Text)).Count();
                                if (emptyOptions > 0)
                                {
                                    result.AddError(
                                        $"{emptyOptions} option(s) sans texte détectée(s)",
                                        questionContext,
                                        "Saisissez le texte pour toutes les options ou supprimez celles qui sont vides"
                                    );
                                }
                            }
                        }
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Erreur lors de la désérialisation du questionnaire");
                    result.AddError(
                        "Format du questionnaire invalide",
                        $"{questionnaireContext} - Erreur de désérialisation",
                        "Le contenu du questionnaire ne peut pas être lu. Essayez de le recréer"
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erreur lors de la validation du questionnaire");
                    result.AddError(
                        "Impossible de valider le questionnaire",
                        $"{questionnaireContext} - Erreur technique: {ex.Message}",
                        "Le format du questionnaire est invalide. Essayez de le recréer"
                    );
                }

                questionnaireIndex++;
            }

            // ✅ Validation réussie
            if (result.IsValid && result.Errors.Count == 0)
            {
                _logger.LogInformation(
                    "Validation réussie: {BlockCount} bloc(s), {QuestionnaireCount} questionnaire(s), {QuestionCount} question(s)",
                    result.TotalBlocks,
                    result.QuestionnaireCount,
                    result.TotalQuestions
                );
            }

            return result;
        }
    }
}