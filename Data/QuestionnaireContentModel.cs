using System.ComponentModel.DataAnnotations;

namespace Mooc.Data;

public class QuestionnaireContentModel
{
    public string Title { get; set; } = "Questionnaire";
    
    public List<QuizStructure> Questions { get; set; } = new();
}