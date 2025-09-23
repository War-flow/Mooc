using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Mooc.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Components.Authorization;
using Mooc.Components.Pages.Manager.CMS;

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
        public int CorrectAnswers { get; set; } = 0;
    }

    public partial class CourseStateService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly AuthenticationStateProvider _authenticationStateProvider;
        private readonly Dictionary<string, CourseProgress> _courseProgresses = new();
        private readonly IAutomaticCertificateService _automaticCertificateService;

        public CourseStateService(
            IDbContextFactory<ApplicationDbContext> contextFactory,
            UserManager<ApplicationUser> userManager,
            AuthenticationStateProvider authenticationStateProvider,
            IAutomaticCertificateService automaticCertificateService)
        {
            _contextFactory = contextFactory;
            _userManager = userManager;
            _authenticationStateProvider = authenticationStateProvider;
            _automaticCertificateService = automaticCertificateService;
        }

        // **MÉTHODE MISE À JOUR**: Récupération automatique de l'utilisateur connecté
        public async Task<CourseProgress> GetOrCreateProgressAsync(int coursId, string? userId = null)
        {
            // Si userId n'est pas fourni, récupérer l'utilisateur connecté
            if (string.IsNullOrEmpty(userId))
            {
                userId = await GetCurrentUserIdAsync();
                if (string.IsNullOrEmpty(userId))
                {
                    throw new InvalidOperationException("Aucun utilisateur connecté trouvé");
                }
            }

            var key = $"{coursId}_{userId}";

            if (!_courseProgresses.ContainsKey(key))
            {
                var progress = await LoadProgressFromDatabaseAsync(coursId, userId);
                _courseProgresses[key] = progress;
            }

            return _courseProgresses[key];
        }

        // **NOUVELLE MÉTHODE**: Récupérer l'ID de l'utilisateur connecté
        private async Task<string?> GetCurrentUserIdAsync()
        {
            try
            {
                var authState = await _authenticationStateProvider.GetAuthenticationStateAsync();
                if (authState.User.Identity?.IsAuthenticated == true)
                {
                    var user = await _userManager.GetUserAsync(authState.User);
                    return user?.Id;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur lors de la récupération de l'utilisateur connecté: {ex.Message}");
            }
            return null;
        }

        public async Task SaveProgressAsync(CourseProgress progress)
        {
            // S'assurer qu'on a un UserId valide
            if (string.IsNullOrEmpty(progress.UserId))
            {
                progress.UserId = await GetCurrentUserIdAsync();
                if (string.IsNullOrEmpty(progress.UserId))
                {
                    throw new InvalidOperationException("Impossible de sauvegarder le progrès sans utilisateur connecté");
                }
            }

            var key = $"{progress.CoursId}_{progress.UserId}";

            progress.CorrectAnswers = CalculateCorrectAnswersFromInteractions(progress.BlockInteractions);
            _courseProgresses[key] = progress;

            await SaveProgressToDatabaseAsync(progress);
        }

        public async Task SaveBlockInteractionAsync(int coursId, int blockIndex, object interactionData, string? userId = null)
        {
            try
            {
                var progress = await GetOrCreateProgressAsync(coursId, userId);

                // ⭐ CORRECTION : Sérialisation améliorée et sécurisée
                var jsonOptions = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    IgnoreNullValues = false
                };

                var jsonData = JsonSerializer.Serialize(interactionData, jsonOptions);

                progress.BlockInteractions[blockIndex] = jsonData;
                progress.LastAccessedBlock = blockIndex;
                progress.LastAccessed = DateTime.UtcNow;

                // ⭐ LOGS DÉTAILLÉS pour débuggage
                Console.WriteLine($"📝 Sauvegarde interaction - CoursId: {coursId}, Block: {blockIndex}");
                Console.WriteLine($"📝 Type données: {interactionData.GetType().Name}");
                Console.WriteLine($"📝 JSON généré: {jsonData}");

                // Vérifier si c'est un score de quiz
                if (jsonData.Contains("scoreResult"))
                {
                    Console.WriteLine("🎯 Score de quiz détecté dans l'interaction");
                }

                await SaveProgressAsync(progress);

                Console.WriteLine("✅ Interaction sauvegardée avec succès");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erreur SaveBlockInteractionAsync: {ex.Message}");
                Console.WriteLine($"❌ StackTrace: {ex.StackTrace}");
                throw;
            }
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

        /// <summary>
        /// Enregistre le résultat d'un quiz avec calcul de score avancé
        /// </summary>
        public async Task SaveQuizResultAsync(
            int coursId,
            int blockIndex,
            QuizStructure quizData,
            bool isCorrect,
            TimeSpan timeSpent = default,
            int hintsUsed = 0,
            int attempts = 1,
            string? userId = null)
        {
            try
            {
                Console.WriteLine($"🎯 Début SaveQuizResultAsync - CoursId: {coursId}, Block: {blockIndex}");

                // Calculer le score du quiz
                var scoreResult = QuizScoring.CalculateScore(
                    quizData.Difficulty,
                    isCorrect,
                    timeSpent,
                    hintsUsed,
                    attempts
                );

                Console.WriteLine($"🎯 Score calculé: {scoreResult.FinalScore}/{scoreResult.BasePoints} pts");

                // Créer l'objet d'interaction enrichi avec une structure plus explicite
                var quizInteraction = new
                {
                    type = "quiz",
                    completed = true,
                    correct = isCorrect,
                    timestamp = DateTime.UtcNow.ToString("O"), // Format ISO 8601
                    difficulty = quizData.Difficulty.ToString(),
                    questionText = quizData.Question ?? "",
                    scoreResult = new
                    {
                        difficulty = scoreResult.Difficulty.ToString(),
                        basePoints = scoreResult.BasePoints,
                        finalScore = scoreResult.FinalScore,
                        performanceLevel = scoreResult.PerformanceLevel.ToString(),
                        performanceMultiplier = scoreResult.PerformanceMultiplier,
                        timeSpentSeconds = scoreResult.TimeSpent.TotalSeconds,
                        hintsUsed = scoreResult.HintsUsed,
                        attempts = scoreResult.Attempts,
                        isCorrect = isCorrect
                    }
                };

                Console.WriteLine($"🎯 Interaction créée avec score: {quizInteraction.scoreResult.finalScore} pts");

                await SaveBlockInteractionAsync(coursId, blockIndex, quizInteraction, userId);

                Console.WriteLine("✅ Quiz result sauvegardé avec succès");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erreur dans SaveQuizResultAsync: {ex.Message}");
                Console.WriteLine($"❌ StackTrace: {ex.StackTrace}");
                throw;
            }
        }

        /// <summary>
        /// Calcule le score total d'un cours
        /// </summary>
        public async Task<CourseScoreResult> CalculateCourseScoreAsync(int coursId, string? userId = null)
        {
            var progress = await GetOrCreateProgressAsync(coursId, userId);
            var quizResults = new List<QuizScoreResult>();

            foreach (var interaction in progress.BlockInteractions)
            {
                try
                {
                    using var document = JsonDocument.Parse(interaction.Value);
                    var root = document.RootElement;

                    if (root.TryGetProperty("scoreResult", out var scoreElement))
                    {
                        var scoreResult = new QuizScoreResult
                        {
                            Difficulty = Enum.Parse<QuizDifficulty>(scoreElement.GetProperty("difficulty").GetString() ?? "Débutant"),
                            IsCorrect = root.GetProperty("correct").GetBoolean(),
                            BasePoints = scoreElement.GetProperty("basePoints").GetInt32(),
                            FinalScore = scoreElement.GetProperty("finalScore").GetInt32(),
                            PerformanceLevel = Enum.Parse<QuizPerformanceLevel>(scoreElement.GetProperty("performanceLevel").GetString() ?? "Average"),
                            PerformanceMultiplier = scoreElement.GetProperty("performanceMultiplier").GetDouble(),
                            TimeSpent = TimeSpan.FromSeconds(scoreElement.GetProperty("timeSpent").GetDouble()),
                            HintsUsed = scoreElement.GetProperty("hintsUsed").GetInt32(),
                            Attempts = scoreElement.GetProperty("attempts").GetInt32()
                        };

                        quizResults.Add(scoreResult);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Erreur lors du parsing du score: {ex.Message}");
                }
            }

            return QuizScoring.CalculateCourseScore(quizResults);
        }

        /// <summary>
        /// Vérifie que les scores sont correctement persistés
        /// </summary>
        public async Task<bool> VerifyScorePersistenceAsync(int coursId, string? userId = null)
        {
            try
            {
                Console.WriteLine($"🔍 Début vérification persistance - CoursId: {coursId}");

                // Vérifier en mémoire
                var memoryProgress = await GetOrCreateProgressAsync(coursId, userId);

                // Vérifier en base de données avec un nouveau contexte
                using var context = await _contextFactory.CreateDbContextAsync();
                var currentUserId = userId ?? await GetCurrentUserIdAsync();
                var dbProgress = await context.CourseProgresses
                    .AsNoTracking()
                    .FirstOrDefaultAsync(cp => cp.CoursId == coursId && cp.UserId == currentUserId);

                if (dbProgress == null)
                {
                    Console.WriteLine("❌ Aucune progression trouvée en BD");
                    return false;
                }

                Console.WriteLine($"✅ Progression trouvée en BD - ID: {dbProgress.Id}");

                if (string.IsNullOrEmpty(dbProgress.BlockInteractions))
                {
                    Console.WriteLine("❌ BlockInteractions vide en BD");
                    return false;
                }

                var dbInteractions = JsonSerializer.Deserialize<Dictionary<int, string>>(dbProgress.BlockInteractions) ?? new Dictionary<int, string>();

                Console.WriteLine($"🔍 Vérification scores - CoursId: {coursId}");
                Console.WriteLine($"🔍 Interactions mémoire: {memoryProgress.BlockInteractions.Count}");
                Console.WriteLine($"🔍 Interactions BD: {dbInteractions.Count}");

                int scoresFoundInMemory = 0;
                int scoresFoundInDB = 0;

                // Vérifier les interactions en mémoire
                foreach (var kvp in memoryProgress.BlockInteractions)
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(kvp.Value);
                        if (doc.RootElement.TryGetProperty("scoreResult", out var scoreElement))
                        {
                            scoresFoundInMemory++;
                            var finalScore = scoreElement.GetProperty("finalScore").GetInt32();
                            Console.WriteLine($"🔍 Score mémoire bloc {kvp.Key}: {finalScore} pts");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️ Erreur parsing score mémoire bloc {kvp.Key}: {ex.Message}");
                    }
                }

                // Vérifier les interactions en BD
                foreach (var kvp in dbInteractions)
                {
                    if (!memoryProgress.BlockInteractions.ContainsKey(kvp.Key))
                    {
                        Console.WriteLine($"⚠️ Interaction en BD manquante en mémoire pour bloc {kvp.Key}");
                    }

                    try
                    {
                        using var doc = JsonDocument.Parse(kvp.Value);
                        if (doc.RootElement.TryGetProperty("scoreResult", out var scoreElement))
                        {
                            scoresFoundInDB++;
                            var finalScore = scoreElement.GetProperty("finalScore").GetInt32();
                            Console.WriteLine($"✅ Score BD bloc {kvp.Key}: {finalScore} pts");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️ Erreur parsing score BD bloc {kvp.Key}: {ex.Message}");
                    }
                }

                Console.WriteLine($"📊 Résumé: {scoresFoundInMemory} scores en mémoire, {scoresFoundInDB} scores en BD");

                var isValid = scoresFoundInMemory == scoresFoundInDB && scoresFoundInDB > 0;
                Console.WriteLine($"🎯 Persistance {(isValid ? "VALIDÉE" : "ÉCHOUÉE")}");

                return isValid;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erreur vérification persistence: {ex.Message}");
                Console.WriteLine($"❌ StackTrace: {ex.StackTrace}");
                return false;
            }
        }

        private async Task<CourseProgress> LoadProgressFromDatabaseAsync(int coursId, string userId)
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

        // Ajoutez le mot-clé 'private' à la méthode pour éviter toute ambiguïté
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
                        UserId = progress.UserId!
                    };
                    context.CourseProgresses.Add(dbProgress);
                }

                var wasCompleted = dbProgress.IsCompleted;

                dbProgress.LastAccessedBlock = progress.LastAccessedBlock;
                dbProgress.CompletedBlocks = JsonSerializer.Serialize(progress.CompletedBlocks);

                // ⭐ CORRECTION PRINCIPALE : S'assurer que BlockInteractions est correctement sérialisé
                var blockInteractionsJson = JsonSerializer.Serialize(progress.BlockInteractions, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });

                dbProgress.BlockInteractions = blockInteractionsJson;
                dbProgress.LastAccessed = progress.LastAccessed;
                dbProgress.IsCompleted = progress.IsCompleted;

                // ⭐ AJOUT : Log pour débuggage amélioré
                Console.WriteLine($"💾 Sauvegarde BD - CoursId: {progress.CoursId}, UserId: {progress.UserId}");
                Console.WriteLine($"💾 BlockInteractions count: {progress.BlockInteractions.Count}");
                Console.WriteLine($"💾 BlockInteractions JSON: {blockInteractionsJson}");

                var changes = await context.SaveChangesAsync();
                Console.WriteLine($"✅ {changes} entité(s) sauvegardée(s) en BD");

                // ⭐ VÉRIFICATION IMMÉDIATE : Confirmer que la sauvegarde a réussi
                var savedProgress = await context.CourseProgresses
                    .AsNoTracking()
                    .FirstOrDefaultAsync(cp => cp.CoursId == progress.CoursId && cp.UserId == progress.UserId);

                if (savedProgress != null)
                {
                    Console.WriteLine($"✅ Progression confirmée en BD - ID: {savedProgress.Id}");
                    Console.WriteLine($"✅ BlockInteractions BD: {savedProgress.BlockInteractions?.Substring(0, Math.Min(100, savedProgress.BlockInteractions?.Length ?? 0))}...");

                    // Vérifier que les scores sont bien dans les données
                    if (!string.IsNullOrEmpty(savedProgress.BlockInteractions))
                    {
                        var interactions = JsonSerializer.Deserialize<Dictionary<int, string>>(savedProgress.BlockInteractions);
                        var scoresCount = interactions?.Values.Count(v => v.Contains("scoreResult")) ?? 0;
                        Console.WriteLine($"✅ {scoresCount} score(s) trouvé(s) en BD");
                    }
                }
                else
                {
                    Console.WriteLine("❌ ERREUR: Progression non trouvée après sauvegarde !");
                }

                // Vérification de génération automatique de certificat
                if (!wasCompleted && progress.IsCompleted && !string.IsNullOrEmpty(progress.UserId))
                {
                    await CheckForAutomaticCertificateGeneration(progress.UserId, progress.CoursId);
                }
                else if (!string.IsNullOrEmpty(progress.UserId))
                {
                    await CheckForAutomaticCertificateGeneration(progress.UserId, progress.CoursId);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erreur critique lors de la sauvegarde du progrès: {ex.Message}");
                Console.WriteLine($"❌ StackTrace: {ex.StackTrace}");

                // ⭐ NOUVEAU : Log détaillé pour débuggage
                Console.WriteLine($"❌ CoursId: {progress.CoursId}, UserId: {progress.UserId}");
                Console.WriteLine($"❌ BlockInteractions count: {progress.BlockInteractions?.Count ?? 0}");

                throw; // Relancer l'exception pour un debugging plus approfondi
            }
        }

        // **NOUVELLE MÉTHODE AMÉLIORÉE**
        private async Task CheckForAutomaticCertificateGeneration(string userId, int coursId)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();

                // Récupérer la session du cours
                var course = await context.Courses
                    .FirstOrDefaultAsync(c => c.Id == coursId);

                if (course?.SessionId != null)
                {
                    // Vérifier et générer le certificat si nécessaire
                    await _automaticCertificateService.CheckAndGenerateCertificateAsync(userId, course.SessionId);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur lors de la vérification de génération automatique de certificat: {ex.Message}");
            }
        }
    }
}