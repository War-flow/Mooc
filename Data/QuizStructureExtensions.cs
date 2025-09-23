namespace Mooc.Data
{
    public static class QuizStructureExtensions
    {
        /// <summary>
        /// Obtient les points de base pour ce quiz selon sa difficulté
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
        /// Obtient la couleur CSS pour les points selon la difficulté
        /// </summary>
        public static string GetPointsClass(this QuizStructure quiz)
        {
            return quiz.Difficulty switch
            {
                QuizDifficulty.Débutant => "text-success",
                QuizDifficulty.Intermédiaire => "text-info",
                QuizDifficulty.Avancé => "text-warning",
                QuizDifficulty.Expert => "text-danger",
                _ => "text-muted"
            };
        }
    }
}