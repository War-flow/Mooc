using Microsoft.Extensions.Caching.Memory;
using Mooc.Data;

namespace Mooc.Services
{
    public interface IScoresCacheService
    {
        Task<CourseScoreResult?> GetCourseScoreAsync(int coursId, string userId);
        Task SetCourseScoreAsync(int coursId, string userId, CourseScoreResult score);
        Task InvalidateCourseScoreAsync(int coursId, string userId);
        Task<Dictionary<int, CourseScoreResult>> GetMultipleCourseScoresAsync(List<int> courseIds, string userId);
        void InvalidateUserScores(string userId);
    }

    public class ScoresCacheService : IScoresCacheService
    {
        private readonly IMemoryCache _cache;
        private readonly CourseStateService _courseStateService;
        private static readonly TimeSpan DefaultCacheExpiration = TimeSpan.FromMinutes(5);

        public ScoresCacheService(IMemoryCache cache, CourseStateService courseStateService)
        {
            _cache = cache;
            _courseStateService = courseStateService;
        }

        public async Task<CourseScoreResult?> GetCourseScoreAsync(int coursId, string userId)
        {
            var cacheKey = GetCacheKey(coursId, userId);
            
            if (_cache.TryGetValue(cacheKey, out CourseScoreResult? cachedScore))
            {
                Console.WriteLine($"🔍 CACHE HIT - Cours {coursId}: {cachedScore.TotalEarnedPoints}/{cachedScore.TotalPossiblePoints} pts");
                return cachedScore;
            }

            Console.WriteLine($"🔍 CACHE MISS - Calcul pour cours {coursId}");
            
            // **CORRECTION PRINCIPALE** : Utiliser CalculateCourseScoreWithTotalAsync pour avoir les vrais totaux
            var scoreWithTotal = await _courseStateService.CalculateCourseScoreWithTotalAsync(coursId, userId);
            
            Console.WriteLine($"🔍 RESULT de CalculateCourseScoreWithTotalAsync - Cours {coursId}:");
            Console.WriteLine($"  - TotalEarnedPoints: {scoreWithTotal.TotalEarnedPoints}");
            Console.WriteLine($"  - TotalPossiblePoints: {scoreWithTotal.TotalPossiblePoints}");
            Console.WriteLine($"  - AttemptedQuizCount: {scoreWithTotal.AttemptedQuizCount}");
            
            // **CORRECTION** : Convertir en CourseScoreResult avec les vrais totaux
            var score = new CourseScoreResult
            {
                TotalEarnedPoints = scoreWithTotal.TotalEarnedPoints,
                TotalPossiblePoints = scoreWithTotal.TotalPossiblePoints, // **VRAI TOTAL**
                ScorePercentage = scoreWithTotal.ScorePercentage,         // **VRAI POURCENTAGE**
                CorrectAnswers = scoreWithTotal.CorrectAnswers,
                QuizResults = scoreWithTotal.QuizResults
            };
            
            Console.WriteLine($"🔍 FINAL CourseScoreResult - Cours {coursId}:");
            Console.WriteLine($"  - TotalEarnedPoints: {score.TotalEarnedPoints}");
            Console.WriteLine($"  - TotalPossiblePoints: {score.TotalPossiblePoints}");
            Console.WriteLine($"  - QuizCount: {score.QuizCount}");
            
            await SetCourseScoreAsync(coursId, userId, score);
            return score;
        }

        public async Task SetCourseScoreAsync(int coursId, string userId, CourseScoreResult score)
        {
            var cacheKey = GetCacheKey(coursId, userId);
            var options = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = DefaultCacheExpiration,
                SlidingExpiration = TimeSpan.FromMinutes(2),
                Priority = CacheItemPriority.Normal
            };
            
            _cache.Set(cacheKey, score, options);
            Console.WriteLine($"🔍 CACHE SET - Cours {coursId}: {score.TotalEarnedPoints}/{score.TotalPossiblePoints} pts");
        }

        public async Task InvalidateCourseScoreAsync(int coursId, string userId)
        {
            var cacheKey = GetCacheKey(coursId, userId);
            _cache.Remove(cacheKey);
            Console.WriteLine($"🔍 CACHE INVALIDATED - Cours {coursId}");
        }

        public async Task<Dictionary<int, CourseScoreResult>> GetMultipleCourseScoresAsync(List<int> courseIds, string userId)
        {
            Console.WriteLine($"🔍 DEBUT GetMultipleCourseScoresAsync - Cours [{string.Join(", ", courseIds)}], User {userId}");
            
            var results = new Dictionary<int, CourseScoreResult>();
            var missingCourseIds = new List<int>();

            // Vérifier le cache d'abord
            foreach (var courseId in courseIds)
            {
                var cacheKey = GetCacheKey(courseId, userId);
                if (_cache.TryGetValue(cacheKey, out CourseScoreResult? cachedScore) && cachedScore != null)
                {
                    Console.WriteLine($"🔍 CACHE HIT - Cours {courseId}: {cachedScore.TotalEarnedPoints}/{cachedScore.TotalPossiblePoints} pts");
                    results[courseId] = cachedScore;
                }
                else
                {
                    Console.WriteLine($"🔍 CACHE MISS - Cours {courseId} à calculer");
                    missingCourseIds.Add(courseId);
                }
            }

            // Charger les scores manquants
            foreach (var courseId in missingCourseIds)
            {
                try
                {
                    Console.WriteLine($"🔍 CALCUL SCORE - Cours {courseId}");
                    
                    // **CORRECTION PRINCIPALE** : Utiliser CalculateCourseScoreWithTotalAsync pour avoir les vrais totaux
                    var scoreWithTotal = await _courseStateService.CalculateCourseScoreWithTotalAsync(courseId, userId);
                    
                    Console.WriteLine($"🔍 RESULT de CalculateCourseScoreWithTotalAsync - Cours {courseId}:");
                    Console.WriteLine($"  - TotalEarnedPoints: {scoreWithTotal.TotalEarnedPoints}");
                    Console.WriteLine($"  - TotalPossiblePoints: {scoreWithTotal.TotalPossiblePoints}");
                    Console.WriteLine($"  - TotalQuizCount: {scoreWithTotal.TotalQuizCount}");
                    
                    // **CORRECTION** : Convertir en CourseScoreResult avec les vrais totaux
                    var score = new CourseScoreResult
                    {
                        TotalEarnedPoints = scoreWithTotal.TotalEarnedPoints,
                        TotalPossiblePoints = scoreWithTotal.TotalPossiblePoints, // **VRAI TOTAL**
                        ScorePercentage = scoreWithTotal.ScorePercentage,         // **VRAI POURCENTAGE**
                        CorrectAnswers = scoreWithTotal.CorrectAnswers,
                        QuizResults = scoreWithTotal.QuizResults
                    };
                    
                    Console.WriteLine($"🔍 FINAL CourseScoreResult - Cours {courseId}:");
                    Console.WriteLine($"  - TotalEarnedPoints: {score.TotalEarnedPoints}");
                    Console.WriteLine($"  - TotalPossiblePoints: {score.TotalPossiblePoints}");
                    Console.WriteLine($"  - QuizCount: {score.QuizCount}");
                    
                    results[courseId] = score;
                    await SetCourseScoreAsync(courseId, userId, score);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Erreur lors du chargement du score pour le cours {courseId}: {ex.Message}");
                    
                    // **AMÉLIORATION** : En cas d'erreur, essayer d'obtenir au moins le nombre total de quiz
                    try
                    {
                        Console.WriteLine($"🔧 FALLBACK - Analyse directe du cours {courseId}");
                        var (totalQuestions, totalPossiblePoints) = await _courseStateService.GetQuestionnaireInfoAsync(courseId);


                        Console.WriteLine($"🔧 FALLBACK RESULT - Cours {courseId}: {totalPossiblePoints} points");
                        
                        results[courseId] = new CourseScoreResult
                        {
                            TotalEarnedPoints = 0,
                            TotalPossiblePoints = totalPossiblePoints, // **VRAI TOTAL**
                            ScorePercentage = 0,
                            CorrectAnswers = 0,
                        };
                    }
                    catch
                    {
                        Console.WriteLine($"❌ FALLBACK FAILED - Cours {courseId}");
                        results[courseId] = new CourseScoreResult
                        {
                            TotalEarnedPoints = 0,
                            TotalPossiblePoints = 0,
                            ScorePercentage = 0,
                            CorrectAnswers = 0,
                        }; // Score vide par défaut
                    }
                }
            }

            Console.WriteLine($"🔍 FINAL GetMultipleCourseScoresAsync RESULT:");
            foreach (var kvp in results)
            {
                Console.WriteLine($"  - Cours {kvp.Key}: {kvp.Value.TotalEarnedPoints}/{kvp.Value.TotalPossiblePoints} pts");
            }

            return results;
        }

        public void InvalidateUserScores(string userId)
        {
            // Note: MemoryCache ne supporte pas la suppression par pattern
            // Dans une implémentation production, on pourrait utiliser Redis
            // Pour l'instant, on va devoir attendre l'expiration naturelle
            Console.WriteLine($"🔍 INVALIDATE USER SCORES - User {userId}");
        }

        private static string GetCacheKey(int coursId, string userId) => $"course_score_{coursId}_{userId}";
    }
}