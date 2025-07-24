using Microsoft.EntityFrameworkCore;
using Mooc.Data;
using System.Text.Json;

namespace Mooc.Services
{
    public class CourseProgress
    {
        public int Id { get; set; }
        public int CoursId { get; set; }
        public string? UserId { get; set; }
        public int LastAccessedBlock { get; set; }
        public HashSet<int> CompletedBlocks { get; set; } = new();
        public Dictionary<int, string> BlockInteractions { get; set; } = new();
        public DateTime LastAccessed { get; set; }
        public bool IsCompleted { get; set; }

        // **NOUVELLE PROPRIÉTÉ**: Compteur des bonnes réponses
        public int CorrectAnswers { get; set; } = 0;
    }

    public class CourseStateService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private readonly Dictionary<string, CourseProgress> _courseProgresses = new();

        public CourseStateService(IDbContextFactory<ApplicationDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public async Task<CourseProgress> GetOrCreateProgressAsync(int coursId, string? userId = null)
        {
            var key = $"{coursId}_{userId ?? "anonymous"}";

            if (!_courseProgresses.ContainsKey(key))
            {
                var progress = await LoadProgressFromDatabaseAsync(coursId, userId);
                _courseProgresses[key] = progress;
            }

            return _courseProgresses[key];
        }

        public async Task SaveProgressAsync(CourseProgress progress)
        {
            var key = $"{progress.CoursId}_{progress.UserId ?? "anonymous"}";
            
            // **CORRECTION** : Recalculer les bonnes réponses avant la sauvegarde
            progress.CorrectAnswers = CalculateCorrectAnswersFromInteractions(progress.BlockInteractions);
            
            _courseProgresses[key] = progress;

            // Sauvegarder en base de données
            await SaveProgressToDatabaseAsync(progress);
        }

        public async Task SaveBlockInteractionAsync(int coursId, int blockIndex, object interactionData, string? userId = null)
        {
            var progress = await GetOrCreateProgressAsync(coursId, userId);
            progress.BlockInteractions[blockIndex] = JsonSerializer.Serialize(interactionData);
            progress.LastAccessedBlock = blockIndex;
            progress.LastAccessed = DateTime.UtcNow;

            await SaveProgressAsync(progress);
        }

        public async Task<T?> GetBlockInteractionAsync<T>(int coursId, int blockIndex, string? userId = null)
        {
            var progress = await GetOrCreateProgressAsync(coursId, userId);

            if (progress.BlockInteractions.TryGetValue(blockIndex, out var data))
            {
                try
                {
                    return JsonSerializer.Deserialize<T>(data);
                }
                catch
                {
                    return default(T);
                }
            }

            return default(T);
        }

        private async Task<CourseProgress> LoadProgressFromDatabaseAsync(int coursId, string? userId)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                var progress = await context.CourseProgresses
                    .FirstOrDefaultAsync(cp => cp.CoursId == coursId && cp.UserId == userId);

                if (progress != null)
                {
                    return new CourseProgress
                    {
                        Id = progress.Id,
                        CoursId = progress.CoursId,
                        UserId = progress.UserId,
                        LastAccessedBlock = progress.LastAccessedBlock,
                        CompletedBlocks = JsonSerializer.Deserialize<HashSet<int>>(progress.CompletedBlocks ?? "[]") ?? new HashSet<int>(),
                        BlockInteractions = JsonSerializer.Deserialize<Dictionary<int, string>>(progress.BlockInteractions ?? "{}") ?? new Dictionary<int, string>(),
                        LastAccessed = progress.LastAccessed,
                        IsCompleted = progress.IsCompleted,
                        // **NOUVEAU**: Calculer les bonnes réponses à partir des interactions
                        CorrectAnswers = CalculateCorrectAnswersFromInteractions(
                            JsonSerializer.Deserialize<Dictionary<int, string>>(progress.BlockInteractions ?? "{}") ?? new Dictionary<int, string>()
                        )
                    };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur lors du chargement du progrès: {ex.Message}");
            }

            return new CourseProgress
            {
                CoursId = coursId,
                UserId = userId,
                LastAccessedBlock = 0,
                CompletedBlocks = new HashSet<int>(),
                BlockInteractions = new Dictionary<int, string>(),
                LastAccessed = DateTime.UtcNow,
                IsCompleted = false,
                CorrectAnswers = 0
            };
        }

        // **NOUVELLE MÉTHODE**: Calculer les bonnes réponses à partir des interactions
        private int CalculateCorrectAnswersFromInteractions(Dictionary<int, string> interactions)
        {
            int correctCount = 0;

            foreach (var interaction in interactions)
            {
                try
                {
                    using var document = JsonDocument.Parse(interaction.Value);
                    if (document.RootElement.TryGetProperty("correct", out var correctProperty) &&
                        correctProperty.GetBoolean())
                    {
                        correctCount++;
                    }
                }
                catch
                {
                    // Ignorer les interactions mal formées
                }
            }

            return correctCount;
        }

        private async Task SaveProgressToDatabaseAsync(CourseProgress progress)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();

                var dbProgress = await context.CourseProgresses
                    .FirstOrDefaultAsync(cp => cp.CoursId == progress.CoursId && cp.UserId == progress.UserId);

                if (dbProgress == null)
                {
                    dbProgress = new Data.CourseProgress
                    {
                        CoursId = progress.CoursId,
                        UserId = progress.UserId
                    };
                    context.CourseProgresses.Add(dbProgress);
                }

                dbProgress.LastAccessedBlock = progress.LastAccessedBlock;
                dbProgress.CompletedBlocks = JsonSerializer.Serialize(progress.CompletedBlocks);
                dbProgress.BlockInteractions = JsonSerializer.Serialize(progress.BlockInteractions);
                dbProgress.LastAccessed = progress.LastAccessed;
                dbProgress.IsCompleted = progress.IsCompleted;

                await context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur lors de la sauvegarde du progrès: {ex.Message}");
            }
        }
    }
}