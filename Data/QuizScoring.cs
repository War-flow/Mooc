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
            
            // D�terminer le niveau de performance (pour l'affichage seulement)
            result.PerformanceLevel = DeterminePerformanceLevel(timeSpent, hintsUsed, attempts, difficulty);

            // Plus de multiplicateur - score final = points de base
            result.PerformanceMultiplier = 1.0;
            result.FinalScore = result.BasePoints;

            return result;
        }

        /// <summary>
        /// D�termine le niveau de performance bas� sur les m�triques (pour affichage uniquement)
        /// </summary>
        private static QuizPerformanceLevel DeterminePerformanceLevel(
            TimeSpan timeSpent, 
            int hintsUsed, 
            int attempts, 
            QuizDifficulty difficulty)
        {
            // Temps de r�f�rence selon la difficult�
            var referenceTime = difficulty switch
            {
                QuizDifficulty.D�butant => TimeSpan.FromSeconds(30),
                QuizDifficulty.Interm�diaire => TimeSpan.FromSeconds(60),
                QuizDifficulty.Avanc� => TimeSpan.FromSeconds(90),
                QuizDifficulty.Expert => TimeSpan.FromSeconds(120),
                _ => TimeSpan.FromSeconds(60)
            };

            // Performance parfaite : premi�re tentative, pas d'indices, temps optimal
            if (attempts == 1 && hintsUsed == 0 && timeSpent <= referenceTime)
            {
                return QuizPerformanceLevel.Perfect;
            }

            // Excellente performance : premi�re tentative, peu ou pas d'indices
            if (attempts == 1 && hintsUsed <= 1 && timeSpent <= referenceTime * 1.5)
            {
                return QuizPerformanceLevel.Excellent;
            }

            // Bonne performance : r�ussi sans trop de difficult�s
            if (attempts <= 2 && hintsUsed <= 2)
            {
                return QuizPerformanceLevel.Good;
            }

            // Performance moyenne
            return QuizPerformanceLevel.Average;
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
}