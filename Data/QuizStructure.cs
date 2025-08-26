namespace Mooc.Data
{
    public class QuizStructure
    {
        public string? Question { get; set; }
        public List<Option> Options { get; set; } = new();
        public string? Type { get; set; }
        public bool ShowProgress { get; set; }
        public bool ShowStats { get; set; }
        public bool AllowHint { get; set; }
        public string? Hint { get; set; }
        public bool ShowValidationFeedback { get; set; }
        public bool ShowCorrectAnswer { get; set; }
        public string? Explanation { get; set; }
    }

    public class Option
    {
        public string Text { get; set; } = string.Empty;
        public bool IsCorrect { get; set; }
    }
}

