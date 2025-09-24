using System.ComponentModel.DataAnnotations;

namespace Mooc.Data
{
    public static class QuizScoring
    {
        // D�finition des points par niveau de difficult�
        public static readonly Dictionary<QuizDifficulty, int> DifficultyPoints = new()
        {
            { QuizDifficulty.D�butant, 2 },
            { QuizDifficulty.Interm�diaire, 4 },
            { QuizDifficulty.Avanc�, 6 },
            { QuizDifficulty.Expert, 8 }
        };

        /// <summary>
        /// Calcule le score d'un quiz bas� uniquement sur la difficult�
        /// </summary>
        /// <param name="difficulty">Niveau de difficult� du quiz</param>
        /// <param name="isCorrect">Si la r�ponse est correcte</param>
        /// <param name="timeSpent">Temps pass� sur le quiz</param>
        /// <param name="hintsUsed">Nombre d'indices utilis�s</param>
        /// <param name="attempts">Nombre de tentatives</param>
        /// <returns>Score calcul�</returns>
        public static QuizScoreResult CalculateScore(
            QuizDifficulty difficulty,
            bool isCorrect,
            TimeSpan timeSpent = default,
            int hintsUsed = 0,
            int attempts = 1)
        {
            var result = new QuizScoreResult
            {
                Difficulty = difficulty,
                IsCorrect = isCorrect,
                TimeSpent = timeSpent,
                HintsUsed = hintsUsed,
                Attempts = attempts
            };

            if (!isCorrect)
            {
                result.BasePoints = 0;
                result.FinalScore = 0;
                result.PerformanceLevel = QuizPerformanceLevel.Average;
                return result;
            }

            // Points de base selon la difficult� (plus de bonus)
            result.BasePoints = DifficultyPoints[difficulty];

            result.FinalScore = result.BasePoints;

            return result;
        }


        /// <summary>
        /// Calcule le score total d'un cours
        /// </summary>
        public static CourseScoreResult CalculateCourseScore(List<QuizScoreResult> quizResults)
        {
            var result = new CourseScoreResult
            {
                QuizResults = quizResults,
                TotalPossiblePoints = quizResults.Sum(q => DifficultyPoints[q.Difficulty]),
                TotalEarnedPoints = quizResults.Sum(q => q.FinalScore),
                QuizCount = quizResults.Count,
                CorrectAnswers = quizResults.Count(q => q.IsCorrect)
            };

            // Calcul du pourcentage bas� sur les points possibles
            result.ScorePercentage = result.TotalPossiblePoints > 0 
                ? (double)result.TotalEarnedPoints / result.TotalPossiblePoints * 100 
                : 0;

            // D�termination du niveau global
            result.OverallLevel = result.ScorePercentage switch
            {
                >= 90 => CoursePerformanceLevel.Excellent,
                >= 75 => CoursePerformanceLevel.Good,
                >= 60 => CoursePerformanceLevel.Average,
                _ => CoursePerformanceLevel.NeedsImprovement
            };

            return result;
        }

        /// <summary>
        /// Calcule le score total pour plusieurs cours de mani�re optimis�e
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

            // D�termination du niveau global de session
            result.OverallLevel = result.ScorePercentage switch
            {
                >= 90 => SessionPerformanceLevel.Excellent,
                >= 75 => SessionPerformanceLevel.Good,
                >= 60 => SessionPerformanceLevel.Average,
                _ => SessionPerformanceLevel.NeedsImprovement
            };

            return result;
        }

        /// <summary>
        /// M�thode utilitaire pour obtenir rapidement les totaux
        /// </summary>
        public static (int earnedPoints, int possiblePoints, double percentage) GetQuickScoreSummary(
            List<CourseScoreResult> courseResults)
        {
            var earnedPoints = courseResults.Sum(c => c.TotalEarnedPoints);
            var possiblePoints = courseResults.Sum(c => c.TotalPossiblePoints);
            var percentage = possiblePoints > 0 ? (double)earnedPoints / possiblePoints * 100 : 0;
            
            return (earnedPoints, possiblePoints, percentage);
        }
    }

    // �num�rations pour les niveaux de performance (conserv�es pour l'affichage)
    public enum QuizPerformanceLevel
    {
        Perfect,
        Excellent,
        Good,
        Average
    }

    public enum CoursePerformanceLevel
    {
        Excellent,
        Good,
        Average,
        NeedsImprovement
    }

    public enum SessionPerformanceLevel
    {
        Excellent,
        Good,
        Average,
        NeedsImprovement
    }

    // Classe pour stocker le r�sultat d'un quiz
    public class QuizScoreResult
    {
        public QuizDifficulty Difficulty { get; set; }
        public bool IsCorrect { get; set; }
        public int BasePoints { get; set; }
        public double PerformanceMultiplier { get; set; } = 1.0;
        public int FinalScore { get; set; }
        public QuizPerformanceLevel PerformanceLevel { get; set; }
        public TimeSpan TimeSpent { get; set; }
        public int HintsUsed { get; set; }
        public int Attempts { get; set; }
    }

    // Classe pour stocker le r�sultat d'un cours
    public class CourseScoreResult
    {
        public List<QuizScoreResult> QuizResults { get; set; } = new();
        public int TotalEarnedPoints { get; set; }
        public int TotalPossiblePoints { get; set; }
        public double ScorePercentage { get; set; }
        public int QuizCount { get; set; }
        public int CorrectAnswers { get; set; }
        public CoursePerformanceLevel OverallLevel { get; set; }
    }

    // Nouvelle classe pour les r�sultats de session
    public class SessionScoreResult
    {
        public List<CourseScoreResult> CourseResults { get; set; } = new();
        public int TotalEarnedPoints { get; set; }
        public int TotalPossiblePoints { get; set; }
        public double ScorePercentage { get; set; }
        public int CourseCount { get; set; }
        public int CompletedCourses { get; set; }
        public SessionPerformanceLevel OverallLevel { get; set; }
    }
}