using System.ComponentModel.DataAnnotations;

namespace Mooc.Data
{
    public static class QuizScoring
    {
        // **SIMPLIFIÉ** : 1 point par question réussie
        public const int PointsPerQuiz = 1;

        /// <summary>
        /// Calcule le score d'une question de manière simple : 1 point si correct, 0 sinon
        /// </summary>
        public static QuizScoreResult CalculateScore(bool isCorrect)
        {
            return new QuizScoreResult
            {
                IsCorrect = isCorrect,
                FinalScore = isCorrect ? PointsPerQuiz : 0
            };
        }

        /// <summary>
        /// Calcule le score total d'un cours basé sur les réponses aux questions du questionnaire
        /// </summary>
        public static CourseScoreResult CalculateCourseScore(List<QuizScoreResult> quizResults)
        {
            var result = new CourseScoreResult
            {
                QuizResults = quizResults,
                TotalEarnedPoints = quizResults.Count(q => q.IsCorrect) * PointsPerQuiz,
                TotalPossiblePoints = quizResults.Count * PointsPerQuiz,
                QuizCount = quizResults.Count,
                CorrectAnswers = quizResults.Count(q => q.IsCorrect)
            };

            // Calcul du pourcentage
            result.ScorePercentage = result.TotalPossiblePoints > 0 
                ? (double)result.TotalEarnedPoints / result.TotalPossiblePoints * 100 
                : 0;

            return result;
        }

        /// <summary>
        /// Calcule le score total pour plusieurs cours
        /// </summary>
        public static SessionScoreResult CalculateSessionScore(List<CourseScoreResult> courseResults)
        {
            var result = new SessionScoreResult
            {
                CourseResults = courseResults,
                TotalEarnedPoints = courseResults.Sum(c => c.TotalEarnedPoints),
                TotalPossiblePoints = courseResults.Sum(c => c.TotalPossiblePoints),
                CourseCount = courseResults.Count,
                CompletedCourses = courseResults.Count(c => c.QuizCount > 0 && c.CorrectAnswers > 0)
            };

            // Calcul du pourcentage global
            result.ScorePercentage = result.TotalPossiblePoints > 0 
                ? (double)result.TotalEarnedPoints / result.TotalPossiblePoints * 100 
                : 0;

            return result;
        }
    }

    // **SIMPLIFIÉ** : Classe de résultat minimaliste
    public class QuizScoreResult
    {
        public bool IsCorrect { get; set; }
        public int FinalScore { get; set; }
    }

    // **SIMPLIFIÉ** : Résultat de cours sans niveaux de performance
    public class CourseScoreResult
    {
        public List<QuizScoreResult> QuizResults { get; set; } = new();
        public int TotalEarnedPoints { get; set; }
        public int TotalPossiblePoints { get; set; }
        public double ScorePercentage { get; set; }
        public int QuizCount { get; set; }
        public int CorrectAnswers { get; set; }
    }

    // **SIMPLIFIÉ** : Résultat de session
    public class SessionScoreResult
    {
        public List<CourseScoreResult> CourseResults { get; set; } = new();
        public int TotalEarnedPoints { get; set; }
        public int TotalPossiblePoints { get; set; }
        public double ScorePercentage { get; set; }
        public int CourseCount { get; set; }
        public int CompletedCourses { get; set; }
    }
}