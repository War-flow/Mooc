namespace Mooc.Data
{
    public static class QuizStructureExtensions
    {
        /// <summary>
        /// Obtient les points de base pour ce quiz selon sa difficult�
        /// </summary>
        public static int GetBasePoints(this QuizStructure quiz)
        {
            return QuizScoring.DifficultyPoints[quiz.Difficulty];
        }

        /// <summary>
        /// Obtient la description des points pour l'affichage
        /// </summary>
        public static string GetPointsDescription(this QuizStructure quiz)
        {
            var basePoints = quiz.GetBasePoints();
            return $"{basePoints} pts (base) | Bonus possible selon performance";
        }

        /// <summary>
        /// Obtient la couleur CSS pour les points selon la difficult�
        /// </summary>
        public static string GetPointsClass(this QuizStructure quiz)
        {
            return quiz.Difficulty switch
            {
                QuizDifficulty.D�butant => "text-success",
                QuizDifficulty.Interm�diaire => "text-info",
                QuizDifficulty.Avanc� => "text-warning",
                QuizDifficulty.Expert => "text-danger",
                _ => "text-muted"
            };
        }
    }
}