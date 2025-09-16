using System.ComponentModel.DataAnnotations;

namespace Mooc.Data
{
    public class QuizStructure
    {
        [Required(ErrorMessage = "La question est obligatoire")]
        [MaxLength(500, ErrorMessage = "La question ne peut pas dépasser 500 caractères")]
        public string? Question { get; set; }

        public List<Option> Options { get; set; } = new();

        [Required(ErrorMessage = "Le type de quiz est obligatoire")]
        public string? Type { get; set; } = "multiple-choice";

        public bool ShowProgress { get; set; }
        public bool ShowStats { get; set; }
        public bool AllowHint { get; set; }

        [MaxLength(200, ErrorMessage = "L'indice ne peut pas dépasser 200 caractères")]
        public string? Hint { get; set; }

        public bool ShowValidationFeedback { get; set; }
        public bool ShowCorrectAnswer { get; set; }

        [MaxLength(300, ErrorMessage = "L'explication ne peut pas dépasser 300 caractères")]
        public string? Explanation { get; set; }

        // Validation personnalisée pour les options
        [CustomValidation(typeof(QuizStructure), nameof(ValidateOptions))]
        public string? OptionsValidation => string.Empty;

        public static ValidationResult? ValidateOptions(string? value, ValidationContext context)
        {
            if (context.ObjectInstance is QuizStructure quiz)
            {
                if (quiz.Options == null || quiz.Options.Count < 2)
                {
                    return new ValidationResult("Au moins deux options sont requises");
                }

                if (!quiz.Options.Any(o => o.IsCorrect))
                {
                    return new ValidationResult("Au moins une option doit être marquée comme correcte");
                }

                if (quiz.Options.Any(o => string.IsNullOrWhiteSpace(o.Text)))
                {
                    return new ValidationResult("Toutes les options doivent avoir un texte");
                }
            }

            return ValidationResult.Success;
        }
    }

    public class Option
    {
        [Required(ErrorMessage = "Le texte de l'option est obligatoire")]
        [MaxLength(200, ErrorMessage = "Le texte de l'option ne peut pas dépasser 200 caractères")]
        public string Text { get; set; } = string.Empty;

        public bool IsCorrect { get; set; }
    }

    // Classe pour le titre du quiz avec validation
    public class QuizBlockContent
    {
        [MaxLength(100, ErrorMessage = "Le titre ne peut pas dépasser 100 caractères")]
        public string? Title { get; set; }

        public QuizStructure QuizData { get; set; } = new();
    }
}

