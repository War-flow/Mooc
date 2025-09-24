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
                return cachedScore;
            }

            var score = await _courseStateService.CalculateCourseScoreAsync(coursId, userId);
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
        }

        public async Task InvalidateCourseScoreAsync(int coursId, string userId)
        {
            var cacheKey = GetCacheKey(coursId, userId);
            _cache.Remove(cacheKey);
        }

        public async Task<Dictionary<int, CourseScoreResult>> GetMultipleCourseScoresAsync(List<int> courseIds, string userId)
        {
            var results = new Dictionary<int, CourseScoreResult>();
            var missingCourseIds = new List<int>();

            // Vérifier le cache d'abord
            foreach (var courseId in courseIds)
            {
                var cacheKey = GetCacheKey(courseId, userId);
                if (_cache.TryGetValue(cacheKey, out CourseScoreResult? cachedScore) && cachedScore != null)
                {
                    results[courseId] = cachedScore;
                }
                else
                {
                    missingCourseIds.Add(courseId);
                }
            }

            // Charger les scores manquants
            foreach (var courseId in missingCourseIds)
            {
                try
                {
                    var score = await _courseStateService.CalculateCourseScoreAsync(courseId, userId);
                    results[courseId] = score;
                    await SetCourseScoreAsync(courseId, userId, score);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Erreur lors du chargement du score pour le cours {courseId}: {ex.Message}");
                    results[courseId] = new CourseScoreResult(); // Score vide par défaut
                }
            }

            return results;
        }

        public void InvalidateUserScores(string userId)
        {
            // Note: MemoryCache ne supporte pas la suppression par pattern
            // Dans une implémentation production, on pourrait utiliser Redis
            // Pour l'instant, on va devoir attendre l'expiration naturelle
        }

        private static string GetCacheKey(int coursId, string userId) => $"course_score_{coursId}_{userId}";
    }
}