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
        private readonly ICertificateEligibilityService _eligibilityService;
        private readonly ICourseValidationService _courseValidationService;
        private readonly ILogger<CourseStateService>? _logger;
        private readonly ICertificateNotificationService _certificateNotificationService;

        public CourseStateService(
            IDbContextFactory<ApplicationDbContext> contextFactory,
            UserManager<ApplicationUser> userManager,
            AuthenticationStateProvider authenticationStateProvider,
            ICertificateEligibilityService eligibilityService,
            ICourseValidationService courseValidationService,
            ICertificateNotificationService certificateNotificationService,
            ILogger<CourseStateService>? logger = null)
        {
            _contextFactory = contextFactory;
            _userManager = userManager;
            _authenticationStateProvider = authenticationStateProvider;
            _eligibilityService = eligibilityService;
            _courseValidationService = courseValidationService;
            _certificateNotificationService = certificateNotificationService;
            _logger = logger;
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
                _logger?.LogError(ex, "Erreur lors de la récupération de l'utilisateur connecté");
            }
            return null;
        }

        /// <summary>
        /// Version sécurisée qui ne lève pas d'exception si aucun utilisateur n'est connecté
        /// </summary>
        public async Task<string?> GetCurrentUserIdSafeAsync()
        {
            try
            {
                var authState = await _authenticationStateProvider.GetAuthenticationStateAsync();
                if (authState.User.Identity?.IsAuthenticated == true)
                {
                    // Vérifier si le UserManager est encore disponible
                    if (_userManager != null)
                    {
                        var user = await _userManager.GetUserAsync(authState.User);
                        return user?.Id;
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                _logger?.LogWarning("UserManager disposé lors de la récupération de l'utilisateur connecté");
                // Essayer d'obtenir l'ID directement depuis les claims
                try
                {
                    var authState = await _authenticationStateProvider.GetAuthenticationStateAsync();
                    if (authState.User.Identity?.IsAuthenticated == true)
                    {
                        return authState.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Impossible d'obtenir l'utilisateur depuis les claims");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Erreur lors de la récupération de l'utilisateur connecté");
            }
            return null;
        }

        // **NOUVELLE MÉTHODE**: Récupérer l'état du cours de manière sécurisée
        public async Task<CourseProgress?> GetOrCreateProgressSafeAsync(int coursId, string? userId = null)
        {
            try
            {
                if (string.IsNullOrEmpty(userId))
                {
                    userId = await GetCurrentUserIdSafeAsync();
                    if (string.IsNullOrEmpty(userId))
                    {
                        _logger?.LogWarning("Aucun utilisateur connecté disponible pour le cours {CourseId}", coursId);
                        return null;
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
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Erreur lors de la récupération du progrès pour le cours {CourseId}", coursId);
                return null;
            }
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
                _logger?.LogInformation("📝 Sauvegarde interaction - CoursId: {CourseId}, Block: {BlockIndex}", coursId, blockIndex);
                _logger?.LogDebug("📝 Type données: {DataType}", interactionData.GetType().Name);
                _logger?.LogDebug("📝 JSON généré: {JsonData}", jsonData);

                // Vérifier si c'est un score de quiz
                if (jsonData.Contains("scoreResult"))
                {
                    _logger?.LogInformation("🎯 Score de quiz détecté dans l'interaction");
                }

                await SaveProgressAsync(progress);

                _logger?.LogInformation("✅ Interaction sauvegardée avec succès");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "❌ Erreur SaveBlockInteractionAsync pour le cours {CourseId}, bloc {BlockIndex}", coursId, blockIndex);
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
                _logger?.LogInformation("🎯 Début SaveQuizResultAsync - CoursId: {CourseId}, Block: {BlockIndex}", coursId, blockIndex);

                // Calculer le score du quiz
                var scoreResult = QuizScoring.CalculateScore(
                    quizData.Difficulty,
                    isCorrect,
                    timeSpent,
                    hintsUsed,
                    attempts
                );

                _logger?.LogInformation("🎯 Score calculé: {FinalScore}/{BasePoints} pts", scoreResult.FinalScore, scoreResult.BasePoints);

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

                _logger?.LogInformation("🎯 Interaction créée avec score: {FinalScore} pts", quizInteraction.scoreResult.finalScore);

                await SaveBlockInteractionAsync(coursId, blockIndex, quizInteraction, userId);

                _logger?.LogInformation("✅ Quiz result sauvegardé avec succès");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "❌ Erreur dans SaveQuizResultAsync pour le cours {CourseId}, bloc {BlockIndex}", coursId, blockIndex);
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
                    _logger?.LogError(ex, "Erreur lors du parsing du score pour le cours {CourseId}", coursId);
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
                _logger?.LogInformation("🔍 Début vérification persistance - CoursId: {CourseId}", coursId);

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
                    _logger?.LogWarning("❌ Aucune progression trouvée en BD");
                    return false;
                }

                _logger?.LogInformation("✅ Progression trouvée en BD - ID: {ProgressId}", dbProgress.Id);

                if (string.IsNullOrEmpty(dbProgress.BlockInteractions))
                {
                    _logger?.LogWarning("❌ BlockInteractions vide en BD");
                    return false;
                }

                var dbInteractions = JsonSerializer.Deserialize<Dictionary<int, string>>(dbProgress.BlockInteractions) ?? new Dictionary<int, string>();

                _logger?.LogInformation("🔍 Vérification scores - CoursId: {CourseId}", coursId);
                _logger?.LogInformation("🔍 Interactions mémoire: {MemoryCount}", memoryProgress.BlockInteractions.Count);
                _logger?.LogInformation("🔍 Interactions BD: {DbCount}", dbInteractions.Count);

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
                            _logger?.LogDebug("🔍 Score mémoire bloc {BlockIndex}: {Score} pts", kvp.Key, finalScore);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "⚠️ Erreur parsing score mémoire bloc {BlockIndex}", kvp.Key);
                    }
                }

                // Vérifier les interactions en BD
                foreach (var kvp in dbInteractions)
                {
                    if (!memoryProgress.BlockInteractions.ContainsKey(kvp.Key))
                    {
                        _logger?.LogWarning("⚠️ Interaction en BD manquante en mémoire pour bloc {BlockIndex}", kvp.Key);
                    }

                    try
                    {
                        using var doc = JsonDocument.Parse(kvp.Value);
                        if (doc.RootElement.TryGetProperty("scoreResult", out var scoreElement))
                        {
                            scoresFoundInDB++;
                            var finalScore = scoreElement.GetProperty("finalScore").GetInt32();
                            _logger?.LogDebug("✅ Score BD bloc {BlockIndex}: {Score} pts", kvp.Key, finalScore);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "⚠️ Erreur parsing score BD bloc {BlockIndex}", kvp.Key);
                    }
                }

                _logger?.LogInformation("📊 Résumé: {MemoryScores} scores en mémoire, {DbScores} scores en BD", scoresFoundInMemory, scoresFoundInDB);

                var isValid = scoresFoundInMemory == scoresFoundInDB && scoresFoundInDB > 0;
                _logger?.LogInformation("🎯 Persistance {Status}", isValid ? "VALIDÉE" : "ÉCHOUÉE");

                return isValid;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "❌ Erreur vérification persistence pour le cours {CourseId}", coursId);
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
                _logger?.LogError(ex, "Erreur lors du chargement du progrès pour le cours {CourseId}, utilisateur {UserId}", coursId, userId);
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
                _logger?.LogDebug("💾 Sauvegarde BD - CoursId: {CourseId}, UserId: {UserId}", progress.CoursId, progress.UserId);
                _logger?.LogDebug("💾 BlockInteractions count: {Count}", progress.BlockInteractions.Count);

                var changes = await context.SaveChangesAsync();
                _logger?.LogDebug("✅ {Changes} entité(s) sauvegardée(s) en BD", changes);

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
                _logger?.LogError(ex, "❌ Erreur critique lors de la sauvegarde du progrès pour le cours {CourseId}, utilisateur {UserId}", progress.CoursId, progress.UserId);
                throw; // Relancer l'exception pour un debugging plus approfondi
            }
        }

        // **NOUVELLE MÉTHODE AMÉLIORÉE** - Utilise maintenant le service d'éligibilité
        private async Task CheckForAutomaticCertificateGeneration(string userId, int coursId)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();

                var course = await context.Courses
                    .FirstOrDefaultAsync(c => c.Id == coursId);

                if (course?.SessionId != null)
                {
                    _logger?.LogInformation("🔍 Vérification certificat automatique - Course {CourseId}, Session {SessionId}, User {UserId}", coursId, course.SessionId, userId);
                    
                    // Vérifier d'abord l'éligibilité
                    var eligibility = await _eligibilityService.CheckCertificateEligibilityAsync(userId, course.SessionId);
                    
                    if (eligibility.IsEligible && !eligibility.HasExistingCertificate)
                    {
                        _logger?.LogInformation("✅ Utilisateur éligible au certificat - Notification de génération");
                        // ⭐ CORRECTION: Utiliser le service de notification
                        await _certificateNotificationService.NotifySessionCompletedAsync(userId, course.SessionId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "❌ Erreur lors de la vérification de génération automatique de certificat pour le cours {CourseId}, utilisateur {UserId}", coursId, userId);
            }
        }

        /// <summary>
        /// Calcule les scores pour plusieurs cours de manière optimisée (batch)
        /// </summary>
        public async Task<Dictionary<int, CourseScoreResult>> CalculateMultipleCourseScoresAsync(
            List<int> courseIds, string? userId = null)
        {
            var currentUserId = userId ?? await GetCurrentUserIdAsync();
            if (string.IsNullOrEmpty(currentUserId))
            {
                throw new InvalidOperationException("Aucun utilisateur connecté trouvé");
            }

            var results = new Dictionary<int, CourseScoreResult>();

            // Traitement en parallèle pour améliorer les performances
            var tasks = courseIds.Select(async courseId =>
            {
                try
                {
                    var score = await CalculateCourseScoreAsync(courseId, currentUserId);
                    return new { CourseId = courseId, Score = score };
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Erreur lors du calcul du score pour le cours {CourseId}", courseId);
                    return new { CourseId = courseId, Score = new CourseScoreResult() };
                }
            });

            var completedTasks = await Task.WhenAll(tasks);
            
            foreach (var result in completedTasks)
            {
                results[result.CourseId] = result.Score;
            }

            return results;
        }

        /// <summary>
        /// Calcule le score total d'une session avec cache
        /// </summary>
        public async Task<SessionScoreResult> CalculateSessionScoreAsync(int sessionId, string? userId = null)
        {
            var currentUserId = userId ?? await GetCurrentUserIdAsync();
            if (string.IsNullOrEmpty(currentUserId))
            {
                throw new InvalidOperationException("Aucun utilisateur connecté trouvé");
            }

            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                var courses = await context.Courses
                    .Where(c => c.SessionId == sessionId)
                    .Select(c => c.Id)
                    .ToListAsync();

                var courseScores = await CalculateMultipleCourseScoresAsync(courses, currentUserId);
                var courseResults = courseScores.Values.ToList();

                return QuizScoring.CalculateSessionScore(courseResults);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Erreur lors du calcul du score de session {SessionId}", sessionId);
                return new SessionScoreResult();
            }
        }

        /// <summary>
        /// Obtient un résumé rapide des scores sans calculs détaillés
        /// </summary>
        public async Task<ScoreSummary> GetScoreSummaryAsync(int coursId, string? userId = null)
        {
            var currentUserId = userId ?? await GetCurrentUserIdAsync();
            if (string.IsNullOrEmpty(currentUserId))
            {
                return new ScoreSummary();
            }

            try
            {
                var progress = await GetOrCreateProgressAsync(coursId, currentUserId);
                var summary = new ScoreSummary
                {
                    CoursId = coursId,
                    UserId = currentUserId,
                    TotalQuizzes = 0,
                    CompletedQuizzes = 0,
                    TotalPointsEarned = 0,
                    TotalPointsPossible = 0
                };

                foreach (var interaction in progress.BlockInteractions.Values)
                {
                    try
                    {
                        using var document = JsonDocument.Parse(interaction);
                        var root = document.RootElement;

                        if (root.TryGetProperty("type", out var typeElement) && 
                            typeElement.GetString() == "quiz")
                        {
                            summary.TotalQuizzes++;

                            if (root.TryGetProperty("scoreResult", out var scoreElement))
                            {
                                summary.CompletedQuizzes++;
                                summary.TotalPointsEarned += scoreElement.GetProperty("finalScore").GetInt32();
                                summary.TotalPointsPossible += scoreElement.GetProperty("basePoints").GetInt32();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Erreur lors du parsing de l'interaction pour le cours {CourseId}", coursId);
                    }
                }

                summary.CompletionPercentage = summary.TotalQuizzes > 0 
                    ? (double)summary.CompletedQuizzes / summary.TotalQuizzes * 100 
                    : 0;

                summary.ScorePercentage = summary.TotalPointsPossible > 0
                    ? (double)summary.TotalPointsEarned / summary.TotalPointsPossible * 100
                    : 0;

                return summary;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Erreur lors du calcul du résumé de score pour le cours {CourseId}", coursId);
                return new ScoreSummary { CoursId = coursId, UserId = currentUserId ?? "" };
            }
        }

        /// <summary>
        /// Analyse le contenu d'un cours pour compter les quiz disponibles
        /// </summary>
        public async Task<CourseScoreResult> CalculateCourseScoreWithContentAnalysisAsync(int coursId, string? userId = null)
        {
            try
            {
                _logger?.LogInformation("🔍 Analyse du contenu du cours {CourseId}", coursId);
                
                using var context = await _contextFactory.CreateDbContextAsync();
                var course = await context.Courses.FindAsync(coursId);
                
                if (course == null)
                {
                    _logger?.LogWarning("❌ Cours {CourseId} introuvable", coursId);
                    return new CourseScoreResult();
                }
                
                // Analyser le contenu JSON pour trouver les quiz disponibles
                var availableQuizzes = await AnalyzeCourseContentForQuizzesAsync(course.Content);
                _logger?.LogInformation("📊 {QuizCount} quiz trouvés dans le contenu du cours", availableQuizzes.Count);
                
                // Si un userId spécifique est fourni, calculer les scores réalisés
                if (!string.IsNullOrEmpty(userId))
                {
                    var progress = await GetOrCreateProgressSafeAsync(coursId, userId);
                    if (progress != null)
                    {
                        return await CalculateScoreFromProgressAsync(availableQuizzes, progress);
                    }
                }
                
                // Sinon, essayer avec l'utilisateur connecté
                var currentUserId = await GetCurrentUserIdSafeAsync();
                if (!string.IsNullOrEmpty(currentUserId))
                {
                    var progress = await GetOrCreateProgressSafeAsync(coursId, currentUserId);
                    if (progress != null)
                    {
                        return await CalculateScoreFromProgressAsync(availableQuizzes, progress);
                    }
                }
                
                // Si aucun utilisateur n'est disponible, retourner juste les informations du cours
                var result = new CourseScoreResult
                {
                    QuizResults = new List<QuizScoreResult>(),
                    TotalEarnedPoints = 0,
                    TotalPossiblePoints = availableQuizzes.Sum(q => QuizScoring.DifficultyPoints[q.Difficulty]),
                    QuizCount = availableQuizzes.Count,
                    CorrectAnswers = 0,
                    ScorePercentage = 0
                };
                
                _logger?.LogInformation("🏆 Résultat final cours {CourseId}: 0/{PossiblePoints} pts (0.0%) - Aucun utilisateur connecté", coursId, result.TotalPossiblePoints);
                
                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "❌ Erreur analyse cours {CourseId}", coursId);
                return new CourseScoreResult();
            }
        }

        /// <summary>
        /// Méthode helper pour calculer les scores à partir du progrès
        /// </summary>
        private async Task<CourseScoreResult> CalculateScoreFromProgressAsync(List<AvailableQuizInfo> availableQuizzes, CourseProgress progress)
        {
            var completedQuizResults = new List<QuizScoreResult>();
            var totalEarnedPoints = 0;
            var totalPossiblePoints = 0;
            var correctAnswers = 0;
            
            foreach (var availableQuiz in availableQuizzes)
            {
                var blockIndex = availableQuiz.BlockOrder;
                var possiblePoints = QuizScoring.DifficultyPoints[availableQuiz.Difficulty];
                totalPossiblePoints += possiblePoints;
                
                // Vérifier si ce quiz a été complété
                if (progress.BlockInteractions.TryGetValue(blockIndex, out var interactionData))
                {
                    try
                    {
                        using var document = JsonDocument.Parse(interactionData);
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
                                TimeSpent = TimeSpan.FromSeconds(scoreElement.GetProperty("timeSpentSeconds").GetDouble()),
                                HintsUsed = scoreElement.GetProperty("hintsUsed").GetInt32(),
                                Attempts = scoreElement.GetProperty("attempts").GetInt32()
                            };

                            completedQuizResults.Add(scoreResult);
                            totalEarnedPoints += scoreResult.FinalScore;
                            
                            if (scoreResult.IsCorrect)
                            {
                                correctAnswers++;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "❌ Erreur parsing interaction bloc {BlockIndex}", blockIndex);
                    }
                }
            }
            
            var result = new CourseScoreResult
            {
                QuizResults = completedQuizResults,
                TotalEarnedPoints = totalEarnedPoints,
                TotalPossiblePoints = totalPossiblePoints,
                QuizCount = availableQuizzes.Count,
                CorrectAnswers = correctAnswers,
                ScorePercentage = totalPossiblePoints > 0 ? (double)totalEarnedPoints / totalPossiblePoints * 100 : 0
            };
            
            // Déterminer le niveau de performance global
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
        /// Version avec validation des quiz avant calcul
        /// </summary>
        public async Task<CourseScoreResult> CalculateCourseScoreWithValidationAsync(int coursId, string? userId = null)
        {
            // Valider d'abord le cours
            var validation = await _courseValidationService.ValidateCourseAsync(coursId);
            
            if (!validation.IsValid)
            {
                _logger?.LogWarning("⚠️ Cours {CourseId} invalide: {Errors}", coursId, string.Join(", ", validation.Errors));
                return new CourseScoreResult
                {
                    QuizCount = validation.QuizCount,
                    // Les autres propriétés restent à 0
                };
            }
            
            _logger?.LogInformation("✅ Cours {CourseId} validé: {QuizCount} quiz trouvés", coursId, validation.QuizCount);
            
            // Continuer avec le calcul normal
            return await CalculateCourseScoreWithContentAnalysisAsync(coursId, userId);
        }

        /// <summary>
        /// Analyse le contenu JSON d'un cours pour extraire les quiz disponibles.
        /// </summary>
        private async Task<List<AvailableQuizInfo>> AnalyzeCourseContentForQuizzesAsync(string? courseContent)
        {
            var quizzes = new List<AvailableQuizInfo>();
            if (string.IsNullOrWhiteSpace(courseContent))
                return quizzes;

            try
            {
                using var document = JsonDocument.Parse(courseContent);
                var root = document.RootElement;

                // Supposons que le contenu du cours contient un tableau "blocks"
                if (root.TryGetProperty("blocks", out var blocksElement) && blocksElement.ValueKind == JsonValueKind.Array)
                {
                    int order = 0;
                    foreach (var block in blocksElement.EnumerateArray())
                    {
                        if (block.TryGetProperty("type", out var typeElement) &&
                            typeElement.GetString() == "quiz")
                        {
                            var difficulty = QuizDifficulty.Débutant;
                            if (block.TryGetProperty("difficulty", out var diffElement))
                            {
                                Enum.TryParse(diffElement.GetString(), out difficulty);
                            }

                            var question = block.TryGetProperty("question", out var qElement)
                                ? qElement.GetString() ?? ""
                                : "";

                            var hasCorrectAnswer = block.TryGetProperty("hasCorrectAnswer", out var correctElement)
                                ? correctElement.GetBoolean()
                                : false;

                            quizzes.Add(new AvailableQuizInfo
                            {
                                BlockOrder = order,
                                Difficulty = difficulty,
                                Question = question,
                                HasCorrectAnswer = hasCorrectAnswer
                            });
                        }
                        order++;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Erreur lors de l'analyse du contenu du cours pour les quiz.");
            }

            return quizzes;
        }
    }

    // Classe légère pour les résumés rapides
    public class ScoreSummary
    {
        public int CoursId { get; set; }
        public string UserId { get; set; } = string.Empty;
        public int TotalQuizzes { get; set; }
        public int CompletedQuizzes { get; set; }
        public int TotalPointsEarned { get; set; }
        public int TotalPointsPossible { get; set; }
        public double CompletionPercentage { get; set; }
        public double ScorePercentage { get; set; }
    }

    // Classe pour stocker les informations des quiz disponibles
    public class AvailableQuizInfo
    {
        public int BlockOrder { get; set; }
        public QuizDifficulty Difficulty { get; set; }
        public string Question { get; set; } = string.Empty;
        public bool HasCorrectAnswer { get; set; }
    }
}