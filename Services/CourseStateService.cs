using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Mooc.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

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
        private readonly ICourseBadgeService? _courseBadgeService;

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

        /// <summary>
        /// **NOUVEAU** : Méthode pour injecter le service de badges après création
        /// </summary>
        public void SetCourseBadgeService(ICourseBadgeService courseBadgeService)
        {
            // Cette méthode sera appelée depuis Program.cs après la création du service
            var fieldInfo = typeof(CourseStateService).GetField("_courseBadgeService", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            fieldInfo?.SetValue(this, courseBadgeService);
        }

        // **MÉTHODE MISE À JOUR**: Récupération automatique de l'utilisateur connecté
        public async Task<CourseProgress> GetOrCreateProgressAsync(int coursId, string? userId = null)
        {
            // Si userId n'est pas fourni, récupérer l'utilisateur connecté
            if (string.IsNullOrEmpty(userId))
            {
                userId = await GetCurrentUserIdSafeAsync();
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

        /// <summary>
        /// **AMÉLIORATION** : Récupération sécurisée de l'ID utilisateur avec fallback sur les claims
        /// </summary>
        private async Task<string?> GetCurrentUserIdSafeAsync()
        {
            try
            {
                var authState = await _authenticationStateProvider.GetAuthenticationStateAsync();
                if (authState?.User?.Identity?.IsAuthenticated != true)
                {
                    return null;
                }

                // **PRIORITÉ 1** : Essayer d'obtenir depuis les claims directement (plus sûr)
                var claimUserId = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                                 authState.User.FindFirst("sub")?.Value;

                if (!string.IsNullOrEmpty(claimUserId))
                {
                    return claimUserId;
                }

                // **PRIORITÉ 2** : Fallback sur UserManager seulement si nécessaire
                try
                {
                    if (_userManager != null)
                    {
                        var user = await _userManager.GetUserAsync(authState.User);
                        return user?.Id;
                    }
                }
                catch (ObjectDisposedException ex)
                {
                    _logger?.LogWarning(ex, "UserManager disposé, utilisation des claims uniquement");
                    // On ignore et retourne null, les claims ont déjà été vérifiés
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
                progress.UserId = await GetCurrentUserIdSafeAsync();
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

                // ⭐ CORRECTION : Sérialisation sécurisée et compatible .NET 9
                var jsonOptions = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull // Remplace IgnoreNullValues (obsolète)
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
        /// **MÉTHODE CORRIGÉE** : Calcule le score total d'un cours avec parsing sécurisé et comptage correct
        /// </summary>
        public async Task<CourseScoreResult> CalculateCourseScoreAsync(int coursId, string? userId = null)
        {
            var progress = await GetOrCreateProgressAsync(coursId, userId);
            var quizResults = new List<QuizScoreResult>();

            // **NOUVELLE APPROCHE** : Analyser d'abord le contenu du cours pour connaître tous les quiz
            var (totalQuizCount, totalPossiblePoints) = await AnalyzeCourseQuizzesAsync(coursId);
            
            _logger?.LogInformation("📊 Analyse cours {CourseId}: {TotalQuizCount} quiz trouvés, {TotalPossiblePoints} points possibles", 
                coursId, totalQuizCount, totalPossiblePoints);

            // Ensuite, récupérer uniquement les quiz tentés depuis les interactions
            foreach (var interaction in progress.BlockInteractions)
            {
                try
                {
                    using var document = JsonDocument.Parse(interaction.Value);
                    var root = document.RootElement;

                    if (root.TryGetProperty("scoreResult", out var scoreElement))
                    {
                        // **PARSING SÉCURISÉ** : Création du QuizScoreResult
                        var scoreResult = new QuizScoreResult();

                        // Propriétés obligatoires avec valeurs par défaut
                        if (scoreElement.TryGetProperty("difficulty", out var difficultyProp))
                        {
                            Enum.TryParse<QuizDifficulty>(difficultyProp.GetString(), out var difficulty);
                            scoreResult.Difficulty = difficulty;
                        }
                        else
                        {
                            scoreResult.Difficulty = QuizDifficulty.Débutant;
                        }

                        if (root.TryGetProperty("correct", out var correctProp))
                        {
                            scoreResult.IsCorrect = correctProp.GetBoolean();
                        }

                        if (scoreElement.TryGetProperty("basePoints", out var basePointsProp))
                        {
                            scoreResult.BasePoints = basePointsProp.GetInt32();
                        }

                        if (scoreElement.TryGetProperty("finalScore", out var finalScoreProp))
                        {
                            scoreResult.FinalScore = finalScoreProp.GetInt32();
                        }

                        if (scoreElement.TryGetProperty("performanceLevel", out var performanceProp))
                        {
                            Enum.TryParse<QuizPerformanceLevel>(performanceProp.GetString(), out var performance);
                            scoreResult.PerformanceLevel = performance;
                        }
                        else
                        {
                            scoreResult.PerformanceLevel = QuizPerformanceLevel.Average;
                        }

                        if (scoreElement.TryGetProperty("performanceMultiplier", out var multiplierProp))
                        {
                            scoreResult.PerformanceMultiplier = multiplierProp.GetDouble();
                        }
                        else
                        {
                            scoreResult.PerformanceMultiplier = 1.0;
                        }

                        if (scoreElement.TryGetProperty("timeSpentSeconds", out var timeProp))
                        {
                            scoreResult.TimeSpent = TimeSpan.FromSeconds(timeProp.GetDouble());
                        }

                        if (scoreElement.TryGetProperty("hintsUsed", out var hintsProp))
                        {
                            scoreResult.HintsUsed = hintsProp.GetInt32();
                        }

                        if (scoreElement.TryGetProperty("attempts", out var attemptsProp))
                        {
                            scoreResult.Attempts = attemptsProp.GetInt32();
                        }
                        else
                        {
                            scoreResult.Attempts = 1;
                        }

                        quizResults.Add(scoreResult);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Erreur lors du parsing du score pour le cours {CourseId}", coursId);
                    // Continue avec les autres interactions au lieu de faire planter le calcul
                }
            }

            _logger?.LogInformation("📋 Quiz tentés: {AttemptedCount}/{TotalCount}", quizResults.Count, totalQuizCount);

            // **CORRECTION PRINCIPALE** : Utiliser les VRAIS totaux du cours, pas seulement les quiz tentés
            var courseScoreResult = QuizScoring.CalculateCourseScore(quizResults);
            
            // **REMPLACER** les totaux par les vrais totaux du cours
            courseScoreResult.TotalPossiblePoints = totalPossiblePoints;
            courseScoreResult.QuizCount = totalQuizCount; // Total de quiz dans le cours, pas seulement tentés
            
            // **RECALCULER** le pourcentage avec les vrais totaux
            courseScoreResult.ScorePercentage = totalPossiblePoints > 0 
                ? (double)courseScoreResult.TotalEarnedPoints / totalPossiblePoints * 100 
                : 0;

            // **RECALCULER** le niveau de performance
            courseScoreResult.OverallLevel = courseScoreResult.ScorePercentage switch
            {
                >= 90 => CoursePerformanceLevel.Excellent,
                >= 75 => CoursePerformanceLevel.Good,
                >= 50 => CoursePerformanceLevel.Average,
                _ => CoursePerformanceLevel.NeedsImprovement
            };

            _logger?.LogInformation("🎯 Score final cours {CourseId}: {EarnedPoints}/{TotalPoints} pts ({Percentage:F1}%)", 
                coursId, courseScoreResult.TotalEarnedPoints, courseScoreResult.TotalPossiblePoints, courseScoreResult.ScorePercentage);

            return courseScoreResult;
        }   

        /// <summary>
        /// **MÉTHODE SIMPLIFIÉE** : Calcule le score avec le nombre total de quiz et points possibles
        /// </summary>
        public async Task<CourseScoreResultWithTotal> CalculateCourseScoreWithTotalAsync(int coursId, string? userId = null)
        {
            // **UTILISER LA MÉTHODE CORRIGÉE** qui inclut déjà les vrais totaux
            var currentScoreResult = await CalculateCourseScoreAsync(coursId, userId);
            
            // Analyser le contenu du cours pour obtenir le total possible (pour double vérification)
            var (totalQuizCount, totalPossiblePoints) = await AnalyzeCourseQuizzesAsync(coursId);
            
            return new CourseScoreResultWithTotal
            {
                TotalEarnedPoints = currentScoreResult.TotalEarnedPoints,
                TotalPossiblePoints = currentScoreResult.TotalPossiblePoints, // Déjà corrigé dans CalculateCourseScoreAsync
                ScorePercentage = currentScoreResult.ScorePercentage,         // Déjà recalculé dans CalculateCourseScoreAsync
                QuizCount = currentScoreResult.QuizCount,                     // Total de quiz dans le cours (corrigé)
                TotalQuizCount = totalQuizCount,                              // Même valeur pour double vérification
                CorrectAnswers = currentScoreResult.CorrectAnswers,
                OverallLevel = currentScoreResult.OverallLevel,               // Déjà recalculé dans CalculateCourseScoreAsync
                QuizResults = currentScoreResult.QuizResults,
                AttemptedQuizCount = currentScoreResult.QuizResults.Count     // Quiz effectivement tentés
            };
        }

        /// <summary>
        /// **ÉTENDUE** : Classe pour retourner toutes les informations de score
        /// </summary>
        public class CourseScoreInfo
        {
            public int TotalEarnedPoints { get; set; }
            public int TotalPossiblePoints { get; set; }
            public double ScorePercentage { get; set; }
            public int QuizCount { get; set; }
            public int CorrectAnswers { get; set; }
            public CoursePerformanceLevel OverallLevel { get; set; }
        }

        /// <summary>
        /// **NOUVELLE MÉTHODE** : Compte le nombre total de quiz dans un cours
        /// </summary>
        public async Task<int> CountQuizzesInCourseAsync(int coursId)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();

                var cours = await context.Courses
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == coursId);

                if (cours == null || string.IsNullOrEmpty(cours.Content))
                {
                    return 0;
                }

                // Analyser le contenu JSON du cours
                var blocks = System.Text.Json.JsonSerializer.Deserialize<List<dynamic>>(cours.Content);
                if (blocks == null) return 0;

                int quizCount = 0;
                foreach (var block in blocks)
                {
                    if (block is JsonElement element &&
                        element.TryGetProperty("Type", out var typeProperty) &&
                        typeProperty.GetString() == "quiz")
                    {
                        quizCount++;
                    }
                }

                return quizCount;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Erreur lors du comptage des quiz pour le cours {CourseId}", coursId);
                return 0;
            }
        }

        /// <summary>
        /// **NOUVELLE MÉTHODE CORRIGÉE AVEC DIAGNOSTIQUE** : Analyse le contenu du cours avec logs détaillés
        /// </summary>
        public async Task<(int totalQuizCount, int totalPossiblePoints)> AnalyzeCourseQuizzesAsync(int coursId)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                
                var cours = await context.Courses
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == coursId);
                    
                if (cours == null || string.IsNullOrEmpty(cours.Content))
                {
                    _logger?.LogWarning("Cours {CourseId} introuvable ou sans contenu", coursId);
                    return (0, 0);
                }
                
                _logger?.LogInformation("🔍 ANALYSE DÉTAILLÉE du cours {CourseId}", coursId);
                _logger?.LogDebug("📄 Contenu brut du cours (taille: {Size} caractères):", cours.Content.Length);
                
                // **ÉTAPE 1** : Analyser la structure JSON générale
                var blocks = System.Text.Json.JsonSerializer.Deserialize<List<JsonElement>>(cours.Content);
                if (blocks == null) 
                {
                    _logger?.LogWarning("Impossible de désérialiser le contenu du cours {CourseId}", coursId);
                    return (0, 0);
                }
                
                _logger?.LogInformation("🔍 {BlockCount} blocs trouvés dans le cours", blocks.Count);
                
                int totalQuizCount = 0;
                int totalPossiblePoints = 0;
                
                for (int blockIndex = 0; blockIndex < blocks.Count; blockIndex++)
                {
                    var block = blocks[blockIndex];
                    
                    if (block.TryGetProperty("Type", out var typeProperty) &&
                        typeProperty.GetString() == "quiz")
                    {
                        totalQuizCount++;
                        
                        _logger?.LogInformation("📝 QUIZ #{QuizNumber} trouvé (bloc {BlockIndex})", totalQuizCount, blockIndex);
                        
                        // **DIAGNOSTIQUE DÉTAILLÉ** : Afficher la structure complète du bloc
                        _logger?.LogDebug("📋 Structure complète du bloc quiz:");
                        _logger?.LogDebug("{BlockJson}", block.ToString());
                        
                        // **ANALYSE DES PROPRIÉTÉS** : Lister toutes les propriétés disponibles
                        _logger?.LogDebug("🔍 Propriétés disponibles dans le bloc:");
                        foreach (var property in block.EnumerateObject())
                        {
                            _logger?.LogDebug("  - {PropertyName}: {PropertyType}", property.Name, property.Value.ValueKind);
                        }
                        
                        // **ÉTAPE 2** : Analyser le contenu du quiz
                        if (block.TryGetProperty("Content", out var contentProperty))
                        {
                            _logger?.LogDebug("📄 Propriété Content trouvée, type: {ValueKind}", contentProperty.ValueKind);
                            
                            var contentRawText = contentProperty.GetRawText();
                            _logger?.LogDebug("📝 Contenu JSON du quiz (taille: {Size}):", contentRawText.Length);
                            
                            // **TENTATIVE 1** : Parsing direct avec QuizStructure amélioré
                            var quizStructure = await TryParseQuizStructure(contentRawText, totalQuizCount);
                            
                            if (quizStructure != null)
                            {
                                var basePoints = quizStructure.GetBasePoints();
                                totalPossiblePoints += basePoints;
                                
                                _logger?.LogInformation("✅ Quiz #{QuizNumber} - Parsing QuizStructure réussi - Difficulté: {Difficulty}, Points: {Points}", 
                                    totalQuizCount, quizStructure.Difficulty, basePoints);
                                continue;
                            }
                            
                            // **TENTATIVE 2** : Parsing manuel amélioré
                            var parsedPoints = ParseDifficultyFromJsonAdvanced(contentRawText, totalQuizCount);
                            totalPossiblePoints += parsedPoints;
                            
                            _logger?.LogInformation("🔧 Quiz #{QuizNumber} - Parsing manuel: {Points} pts", totalQuizCount, parsedPoints);
                        }
                        else
                        {
                            _logger?.LogWarning("⚠️ Quiz #{QuizNumber} - Pas de propriété Content trouvée", totalQuizCount);
                            
                            // **TENTATIVE 3** : Chercher directement dans le bloc
                            var fallbackPoints = ParseDifficultyFromBlockJsonAdvanced(block, totalQuizCount);
                            totalPossiblePoints += fallbackPoints;
                            
                            _logger?.LogInformation("🔧 Quiz #{QuizNumber} - Parsing bloc direct: {Points} pts", totalQuizCount, fallbackPoints);
                        }
                    }
                }
                
                _logger?.LogInformation("📊 RÉSULTAT FINAL - Cours {CourseId}: {QuizCount} quiz, {TotalPoints} points possibles", 
                    coursId, totalQuizCount, totalPossiblePoints);
                
                return (totalQuizCount, totalPossiblePoints);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "❌ Erreur lors de l'analyse des quiz pour le cours {CourseId}", coursId);
                return (0, 0);
            }
        }

        /// <summary>
        /// **PARSING MANUEL AVANCÉ CORRIGÉ** : Gère les JSON strings échappés
        /// </summary>
        private int ParseDifficultyFromJsonAdvanced(string jsonContent, int quizNumber)
        {
            try
            {
                _logger?.LogDebug("🔍 PARSING MANUEL AVANCÉ pour Quiz #{QuizNumber}", quizNumber);
                _logger?.LogDebug("📄 JSON brut reçu: {JsonContent}", jsonContent.Substring(0, Math.Min(200, jsonContent.Length)));
                
                // **CORRECTION PRINCIPALE** : Détecter si c'est une string JSON échappée
                string actualJsonContent = jsonContent;
                
                // Si le JSON commence par une quote, c'est une string échappée
                if (jsonContent.StartsWith("\"") && jsonContent.EndsWith("\""))
                {
                    try
                    {
                        // Désérialiser la string JSON échappée
                        actualJsonContent = JsonSerializer.Deserialize<string>(jsonContent) ?? jsonContent;
                        _logger?.LogDebug("🔧 JSON décodé de string échappée: {ActualContent}", actualJsonContent.Substring(0, Math.Min(200, actualJsonContent.Length)));
                    }
                    catch (JsonException ex)
                    {
                        _logger?.LogWarning("⚠️ Quiz #{QuizNumber} - Erreur décodage string JSON: {Error}", quizNumber, ex.Message);
                        // Continuer avec le contenu original
                    }
                }
                
                // **ANALYSE 1** : Chercher des patterns numériques directement dans le texte
                if (actualJsonContent.Contains("\"Difficulty\":4") || actualJsonContent.Contains("\"difficulty\":4") ||
                    actualJsonContent.Contains("\"Difficulty\": 4") || actualJsonContent.Contains("\"difficulty\": 4"))
                {
                    _logger?.LogInformation("🎯 Quiz #{QuizNumber} - Détecté difficulté Expert (4) via pattern numérique", quizNumber);
                    return QuizScoring.DifficultyPoints[QuizDifficulty.Expert]; // 8 points
                }
                else if (actualJsonContent.Contains("\"Difficulty\":3") || actualJsonContent.Contains("\"difficulty\":3") ||
                         actualJsonContent.Contains("\"Difficulty\": 3") || actualJsonContent.Contains("\"difficulty\": 3"))
                {
                    _logger?.LogInformation("🎯 Quiz #{QuizNumber} - Détecté difficulté Avancé (3) via pattern numérique", quizNumber);
                    return QuizScoring.DifficultyPoints[QuizDifficulty.Avancé]; // 6 points
                }
                else if (actualJsonContent.Contains("\"Difficulty\":2") || actualJsonContent.Contains("\"difficulty\":2") ||
                         actualJsonContent.Contains("\"Difficulty\": 2") || actualJsonContent.Contains("\"difficulty\": 2"))
                {
                    _logger?.LogInformation("🎯 Quiz #{QuizNumber} - Détecté difficulté Intermédiaire (2) via pattern numérique", quizNumber);
                    return QuizScoring.DifficultyPoints[QuizDifficulty.Intermédiaire]; // 4 points
                }
                else if (actualJsonContent.Contains("\"Difficulty\":1") || actualJsonContent.Contains("\"difficulty\":1") ||
                         actualJsonContent.Contains("\"Difficulty\": 1") || actualJsonContent.Contains("\"difficulty\": 1"))
                {
                    _logger?.LogInformation("🎯 Quiz #{QuizNumber} - Détecté difficulté Débutant (1) via pattern numérique", quizNumber);
                    return QuizScoring.DifficultyPoints[QuizDifficulty.Débutant]; // 2 points
                }
                
                // **ANALYSE 2** : Chercher des patterns textuels avec enum values
                if (actualJsonContent.Contains("\"Expert\"") || actualJsonContent.Contains("\"expert\""))
                {
                    _logger?.LogInformation("🎯 Quiz #{QuizNumber} - Détecté difficulté Expert via pattern textuel", quizNumber);
                    return QuizScoring.DifficultyPoints[QuizDifficulty.Expert]; // 8 points
                }
                else if (actualJsonContent.Contains("\"Avancé\"") || actualJsonContent.Contains("\"avancé\"") || 
                         actualJsonContent.Contains("\"Advanced\"") || actualJsonContent.Contains("\"advanced\""))
                {
                    _logger?.LogInformation("🎯 Quiz #{QuizNumber} - Détecté difficulté Avancé via pattern textuel", quizNumber);
                    return QuizScoring.DifficultyPoints[QuizDifficulty.Avancé]; // 6 points
                }
                else if (actualJsonContent.Contains("\"Intermédiaire\"") || actualJsonContent.Contains("\"intermédiaire\"") || 
                         actualJsonContent.Contains("\"Intermediate\"") || actualJsonContent.Contains("\"intermediate\""))
                {
                    _logger?.LogInformation("🎯 Quiz #{QuizNumber} - Détecté difficulté Intermédiaire via pattern textuel", quizNumber);
                    return QuizScoring.DifficultyPoints[QuizDifficulty.Intermédiaire]; // 4 points
                }
                else if (actualJsonContent.Contains("\"Débutant\"") || actualJsonContent.Contains("\"débutant\"") || 
                         actualJsonContent.Contains("\"Beginner\"") || actualJsonContent.Contains("\"beginner\""))
                {
                    _logger?.LogInformation("🎯 Quiz #{QuizNumber} - Détecté difficulté Débutant via pattern textuel", quizNumber);
                    return QuizScoring.DifficultyPoints[QuizDifficulty.Débutant]; // 2 points
                }
                
                // **ANALYSE 3** : Parser le JSON proprement maintenant qu'on a le bon format
                try
                {
                    using var document = JsonDocument.Parse(actualJsonContent);
                    var root = document.RootElement;
                    
                    _logger?.LogDebug("🔍 Propriétés dans le contenu JSON décodé du Quiz #{QuizNumber}:", quizNumber);
                    
                    // Vérifier que c'est bien un objet JSON
                    if (root.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var property in root.EnumerateObject())
                        {
                            _logger?.LogDebug("  - {PropertyName} ({Type}): {PropertyValue}", 
                                property.Name, property.Value.ValueKind, property.Value.ToString());
                            
                            if (property.Name.Equals("Difficulty", StringComparison.OrdinalIgnoreCase) || 
                                property.Name.Equals("difficulty", StringComparison.OrdinalIgnoreCase))
                            {
                                if (property.Value.ValueKind == JsonValueKind.Number)
                                {
                                    var difficultyValue = property.Value.GetInt32();
                                    _logger?.LogInformation("🎯 Quiz #{QuizNumber} - Trouvé Difficulty numérique: {Value}", quizNumber, difficultyValue);
                                    
                                    return difficultyValue switch
                                    {
                                        4 => QuizScoring.DifficultyPoints[QuizDifficulty.Expert],
                                        3 => QuizScoring.DifficultyPoints[QuizDifficulty.Avancé],
                                        2 => QuizScoring.DifficultyPoints[QuizDifficulty.Intermédiaire],
                                        1 => QuizScoring.DifficultyPoints[QuizDifficulty.Débutant],
                                        _ => QuizScoring.DifficultyPoints[QuizDifficulty.Débutant]
                                    };
                                }
                                else if (property.Value.ValueKind == JsonValueKind.String)
                                {
                                    var difficultyText = property.Value.GetString();
                                    _logger?.LogInformation("🎯 Quiz #{QuizNumber} - Trouvé Difficulty textuel: {Value}", quizNumber, difficultyText);
                                    
                                    if (Enum.TryParse<QuizDifficulty>(difficultyText, out var difficulty))
                                    {
                                        return QuizScoring.DifficultyPoints[difficulty];
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        _logger?.LogWarning("⚠️ Quiz #{QuizNumber} - Le JSON décodé n'est pas un objet: {ValueKind}", quizNumber, root.ValueKind);
                    }
                }
                catch (JsonException ex)
                {
                    _logger?.LogWarning("⚠️ Quiz #{QuizNumber} - Erreur parsing JSON décodé: {Error}", quizNumber, ex.Message);
                }
                
                // **ANALYSE 4** : Regex patterns en dernier recours
                _logger?.LogDebug("🔍 Recherche par expressions régulières...");
                
                // Chercher les patterns avec regex
                var difficultyPatterns = new[]
                {
                    (@"""[Dd]ifficulty"":\s*4", QuizDifficulty.Expert),
                    (@"""[Dd]ifficulty"":\s*3", QuizDifficulty.Avancé),
                    (@"""[Dd]ifficulty"":\s*2", QuizDifficulty.Intermédiaire),
                    (@"""[Dd]ifficulty"":\s*1", QuizDifficulty.Débutant),
                    (@"""[Dd]ifficulty"":\s*""Expert""", QuizDifficulty.Expert),
                    (@"""[Dd]ifficulty"":\s*""Avancé""", QuizDifficulty.Avancé),
                    (@"""[Dd]ifficulty"":\s*""Intermédiaire""", QuizDifficulty.Intermédiaire),
                    (@"""[Dd]ifficulty"":\s*""Débutant""", QuizDifficulty.Débutant)
                };
                
                foreach (var (pattern, difficulty) in difficultyPatterns)
                {
                    if (System.Text.RegularExpressions.Regex.IsMatch(actualJsonContent, pattern))
                    {
                        var points = QuizScoring.DifficultyPoints[difficulty];
                        _logger?.LogInformation("🎯 Quiz #{QuizNumber} - Détecté {Difficulty} via regex: {Points} pts", 
                            quizNumber, difficulty, points);
                        return points;
                    }
                }
                
                // Si aucune difficulté trouvée, défaut
                _logger?.LogWarning("⚠️ Quiz #{QuizNumber} - Aucune difficulté détectée dans le JSON, utilisation par défaut: Débutant (2 pts)", quizNumber);
                _logger?.LogDebug("📄 Contenu final non parsé: {Content}", actualJsonContent);
                
                return QuizScoring.DifficultyPoints[QuizDifficulty.Débutant];
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "❌ Quiz #{QuizNumber} - Erreur lors du parsing manuel avancé", quizNumber);
                return QuizScoring.DifficultyPoints[QuizDifficulty.Débutant];
            }
        }

        /// <summary>
        /// **TENTATIVE DE PARSING DIRECT AVEC QUIZSTRUCTURE CORRIGÉE**
        /// </summary>
        private async Task<QuizStructure?> TryParseQuizStructure(string jsonContent, int quizNumber)
        {
            try
            {
                // **CORRECTION** : Gestion des strings JSON échappées
                string actualJsonContent = jsonContent;
                
                if (jsonContent.StartsWith("\"") && jsonContent.EndsWith("\""))
                {
                    try
                    {
                        actualJsonContent = JsonSerializer.Deserialize<string>(jsonContent) ?? jsonContent;
                        _logger?.LogDebug("🔧 Quiz #{QuizNumber} - JSON décodé de string échappée pour QuizStructure", quizNumber);
                    }
                    catch (JsonException)
                    {
                        // Continuer avec le contenu original
                    }
                }

                // **TENTATIVE 1** : Parsing direct avec QuizStructure
                try
                {
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        ReadCommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    };
                    
                    var quizStructure = JsonSerializer.Deserialize<QuizStructure>(actualJsonContent, options);
                    
                    if (quizStructure != null)
                    {
                        _logger?.LogInformation("✅ Quiz #{QuizNumber} - QuizStructure parsing réussi avec options", quizNumber);
                        return quizStructure;
                    }
                }
                catch (JsonException ex)
                {
                    _logger?.LogDebug("🔧 Quiz #{QuizNumber} - Échec parsing QuizStructure avec options: {Error}", quizNumber, ex.Message);
                }

                // **TENTATIVE 2** : Parsing avec QuizBlockContent wrapper
                try
                {
                    var quizBlockContent = JsonSerializer.Deserialize<QuizBlockContent>(actualJsonContent);
                    
                    if (quizBlockContent?.QuizData != null)
                    {
                        _logger?.LogInformation("✅ Quiz #{QuizNumber} - QuizBlockContent parsing réussi", quizNumber);
                        return quizBlockContent.QuizData;
                    }
                }
                catch (JsonException ex)
                {
                    _logger?.LogDebug("🔧 Quiz #{QuizNumber} - Échec parsing QuizBlockContent: {Error}", quizNumber, ex.Message);
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "❌ Quiz #{QuizNumber} - Erreur lors de la tentative de parsing QuizStructure", quizNumber);
                return null;
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
                dbProgress.BlockInteractions = JsonSerializer.Serialize(progress.BlockInteractions, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
                dbProgress.LastAccessed = progress.LastAccessed;
                dbProgress.IsCompleted = progress.IsCompleted;

                _logger?.LogDebug("💾 Sauvegarde BD - CoursId: {CourseId}, UserId: {UserId}", progress.CoursId, progress.UserId);

                var changes = await context.SaveChangesAsync();
                _logger?.LogDebug("✅ {Changes} entité(s) sauvegardée(s) en BD", changes);

                // **NOUVEAU** : Vérifier et attribuer un badge si le cours vient d'être complété
                if (!wasCompleted && progress.IsCompleted && !string.IsNullOrEmpty(progress.UserId))
                {
                    await CheckForBadgeAward(progress.UserId, progress.CoursId);
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
                throw;
            }
        }

        // **NOUVELLE MÉTHODE** : Vérifier et attribuer un badge si éligible
        private async Task CheckForBadgeAward(string userId, int coursId)
        {
            try
            {
                if (_courseBadgeService != null)
                {
                    _logger?.LogInformation("🎯 Vérification d'éligibilité au badge - CoursId: {CoursId}, UserId: {UserId}", coursId, userId);
                    
                    var badge = await _courseBadgeService.EvaluateAndAwardBadgeAsync(userId, coursId);
                    
                    if (badge != null)
                    {
                        _logger?.LogInformation("🏆 Badge {BadgeType} attribué avec succès pour le cours {CoursId}!", badge.BadgeType, coursId);
                    }
                }
                else
                {
                    _logger?.LogWarning("⚠️ CourseBadgeService non disponible pour l'attribution de badge");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "❌ Erreur lors de la vérification d'attribution de badge pour le cours {CourseId}, utilisateur {UserId}", coursId, userId);
            }
        }

        private async Task CheckForAutomaticCertificateGeneration(string userId, int coursId)
        {
            try
            {
                if (_eligibilityService != null && _certificateNotificationService != null)
                {
                    // Remplacer NotifyCertificateAsync par NotifySessionCompletedAsync
                    var eligibilityResult = await _eligibilityService.CheckCertificateEligibilityAsync(userId, coursId);
                    if (eligibilityResult.IsEligible)
                    {
                        await _certificateNotificationService.NotifySessionCompletedAsync(userId, coursId);
                        _logger?.LogInformation("🎓 Certificat généré et notification envoyée pour l'utilisateur {UserId}, cours {CoursId}", userId, coursId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "❌ Erreur lors de la génération automatique du certificat pour l'utilisateur {UserId}, cours {CoursId}", userId, coursId);
            }
        }

        /// <summary>
        /// **NOUVELLE MÉTHODE** : Calcule le score d'un cours avec analyse de contenu
        /// </summary>
        public async Task<CourseScoreInfo> CalculateCourseScoreWithContentAnalysisAsync(int coursId, string? userId = null)
        {
            try
            {
                // Utiliser la méthode existante pour obtenir le résultat détaillé
                var courseScoreResult = await CalculateCourseScoreAsync(coursId, userId);
                
                // **CORRECTION** : Retourner toutes les informations nécessaires
                return new CourseScoreInfo
                {
                    TotalEarnedPoints = courseScoreResult.TotalEarnedPoints,
                    TotalPossiblePoints = courseScoreResult.TotalPossiblePoints,
                    ScorePercentage = courseScoreResult.ScorePercentage,
                    QuizCount = courseScoreResult.QuizCount,
                    CorrectAnswers = courseScoreResult.CorrectAnswers,
                    OverallLevel = courseScoreResult.OverallLevel
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "❌ Erreur lors du calcul du score avec analyse de contenu pour le cours {CourseId}", coursId);
                return new CourseScoreInfo
                {
                    TotalEarnedPoints = 0,
                    TotalPossiblePoints = 0,
                    ScorePercentage = 0,
                    QuizCount = 0,
                    CorrectAnswers = 0,
                    OverallLevel = CoursePerformanceLevel.NeedsImprovement
                };
            }
        }

        /// <summary>
        /// **ÉTENDUE** : Classe pour retourner toutes les informations de score
        /// </summary>
        public class CourseScoreResultWithTotal : CourseScoreResult
        {
            public int TotalQuizCount { get; set; }
            public int AttemptedQuizCount { get; set; }
        }

        /// <summary>
        /// **PARSING MANUEL AVANCÉ POUR BLOC JSON** : Gère les JsonElement pour détecter la difficulté.
        /// </summary>
        private int ParseDifficultyFromBlockJsonAdvanced(JsonElement block, int quizNumber)
        {
            try
            {
                _logger?.LogDebug("🔍 PARSING MANUEL AVANCÉ (bloc) pour Quiz #{QuizNumber}", quizNumber);

                // Chercher la propriété "Difficulty" dans le bloc
                if (block.TryGetProperty("Difficulty", out var difficultyProp) || block.TryGetProperty("difficulty", out difficultyProp))
                {
                    if (difficultyProp.ValueKind == JsonValueKind.Number)
                    {
                        var difficultyValue = difficultyProp.GetInt32();
                        _logger?.LogInformation("🎯 Quiz #{QuizNumber} - Trouvé Difficulty numérique dans bloc: {Value}", quizNumber, difficultyValue);

                        return difficultyValue switch
                        {
                            4 => QuizScoring.DifficultyPoints[QuizDifficulty.Expert],
                            3 => QuizScoring.DifficultyPoints[QuizDifficulty.Avancé],
                            2 => QuizScoring.DifficultyPoints[QuizDifficulty.Intermédiaire],
                            1 => QuizScoring.DifficultyPoints[QuizDifficulty.Débutant],
                            _ => QuizScoring.DifficultyPoints[QuizDifficulty.Débutant]
                        };
                    }
                    else if (difficultyProp.ValueKind == JsonValueKind.String)
                    {
                        var difficultyText = difficultyProp.GetString();
                        _logger?.LogInformation("🎯 Quiz #{QuizNumber} - Trouvé Difficulty textuel dans bloc: {Value}", quizNumber, difficultyText);

                        if (Enum.TryParse<QuizDifficulty>(difficultyText, out var difficulty))
                        {
                            return QuizScoring.DifficultyPoints[difficulty];
                        }
                    }
                }

                // Recherche textuelle fallback
                var blockJson = block.ToString();
                if (blockJson.Contains("\"Expert\"") || blockJson.Contains("\"expert\""))
                    return QuizScoring.DifficultyPoints[QuizDifficulty.Expert];
                if (blockJson.Contains("\"Avancé\"") || blockJson.Contains("\"avancé\"") || blockJson.Contains("\"Advanced\"") || blockJson.Contains("\"advanced\""))
                    return QuizScoring.DifficultyPoints[QuizDifficulty.Avancé];
                if (blockJson.Contains("\"Intermédiaire\"") || blockJson.Contains("\"intermédiaire\"") || blockJson.Contains("\"Intermediate\"") || blockJson.Contains("\"intermediate\""))
                    return QuizScoring.DifficultyPoints[QuizDifficulty.Intermédiaire];
                if (blockJson.Contains("\"Débutant\"") || blockJson.Contains("\"débutant\"") || blockJson.Contains("\"Beginner\"") || blockJson.Contains("\"beginner\""))
                    return QuizScoring.DifficultyPoints[QuizDifficulty.Débutant];

                _logger?.LogWarning("⚠️ Quiz #{QuizNumber} - Aucune difficulté détectée dans le bloc, défaut: Débutant (2 pts)", quizNumber);
                return QuizScoring.DifficultyPoints[QuizDifficulty.Débutant];
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "❌ Quiz #{QuizNumber} - Erreur lors du parsing manuel avancé du bloc", quizNumber);
                return QuizScoring.DifficultyPoints[QuizDifficulty.Débutant];
            }
        }
    }
}

