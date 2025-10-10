using Microsoft.EntityFrameworkCore;
using Mooc.Data;

namespace Mooc.Services
{
    public interface IAutomaticCertificateService
    {
        Task CheckAndGenerateCertificateAsync(string userId, int sessionId);
        Task<bool> IsSessionCompletedByUserAsync(string userId, int sessionId);
        Task<double> CalculateSessionScorePercentageAsync(string userId, int sessionId);
        Task<CertificateEligibilityResult> CheckCertificateEligibilityAsync(string userId, int sessionId);
    }

    public class AutomaticCertificateService : IAutomaticCertificateService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private readonly ICertificateGenerationService _certificateService;
        private readonly ILogger<AutomaticCertificateService> _logger;
        private readonly ICertificateEligibilityService _eligibilityService;

        public AutomaticCertificateService(
            IDbContextFactory<ApplicationDbContext> contextFactory,
            ICertificateGenerationService certificateService,
            ILogger<AutomaticCertificateService> logger,
            ICertificateEligibilityService eligibilityService)
        {
            _contextFactory = contextFactory;
            _certificateService = certificateService;
            _logger = logger;
            _eligibilityService = eligibilityService;
        }

        /// <summary>
        /// Délègue au service d'éligibilité
        /// </summary>
        public async Task<double> CalculateSessionScorePercentageAsync(string userId, int sessionId)
        {
            return await _eligibilityService.CalculateSessionScorePercentageAsync(userId, sessionId);
        }

        /// <summary>
        /// Délègue au service d'éligibilité
        /// </summary>
        public async Task<bool> IsSessionCompletedByUserAsync(string userId, int sessionId)
        {
            return await _eligibilityService.IsSessionCompletedByUserAsync(userId, sessionId);
        }

        /// <summary>
        /// Délègue au service d'éligibilité
        /// </summary>
        public async Task<CertificateEligibilityResult> CheckCertificateEligibilityAsync(string userId, int sessionId)
        {
            return await _eligibilityService.CheckCertificateEligibilityAsync(userId, sessionId);
        }

        public async Task CheckAndGenerateCertificateAsync(string userId, int sessionId)
        {
            try
            {
                _logger.LogInformation("🎓 [CERTIFICAT-AUTO] DÉBUT - Vérification génération automatique pour User: {UserId}, Session: {SessionId}", userId, sessionId);

                using var context = await _contextFactory.CreateDbContextAsync();
                    
                // Utiliser le service d'éligibilité pour vérifier tous les critères
                var eligibilityResult = await _eligibilityService.CheckCertificateEligibilityAsync(userId, sessionId);

                _logger.LogInformation("🎓 [CERTIFICAT-AUTO] Résultat éligibilité - Complétée: {IsCompleted}, Score: {Score}%, MinScore: {HasMinScore}, Éligible: {IsEligible}, Existe: {HasCert}", 
                    eligibilityResult.IsSessionCompleted, 
                    eligibilityResult.SessionScorePercentage.ToString("F1"), 
                    eligibilityResult.HasMinimumScore, 
                    eligibilityResult.IsEligible, 
                    eligibilityResult.HasExistingCertificate);

                if (eligibilityResult.HasExistingCertificate)
                {
                    _logger.LogInformation("🎓 [CERTIFICAT-AUTO] Certificat déjà existant pour l'utilisateur {UserId} et la session {SessionId}", userId, sessionId);
                    return;
                }

                if (!eligibilityResult.IsSessionCompleted)
                {
                    _logger.LogInformation("🎓 [CERTIFICAT-AUTO] Session {SessionId} non complétée pour l'utilisateur {UserId}", sessionId, userId);
                    return;
                }

                if (!eligibilityResult.HasMinimumScore)
                {
                    _logger.LogInformation(
                        "🎓 [CERTIFICAT-AUTO] 🚫 Score insuffisant pour la génération du certificat - Session {SessionId}, Utilisateur {UserId}: {Score}% < 70%",
                        sessionId, userId, eligibilityResult.SessionScorePercentage.ToString("F1"));
                    return;
                }

                _logger.LogInformation(
                    "🎓 [CERTIFICAT-AUTO] ✅ Conditions remplies pour la génération - Session {SessionId}, Utilisateur {UserId}: {Score}% >= 70%",
                    sessionId, userId, eligibilityResult.SessionScorePercentage.ToString("F1"));

                // Récupérer les informations de la session
                var session = await context.Sessions
                    .FirstOrDefaultAsync(s => s.Id == sessionId);

                if (session == null)
                {
                    _logger.LogError("🎓 [CERTIFICAT-AUTO] ❌ Session {SessionId} non trouvée", sessionId);
                    return;
                }

                _logger.LogInformation("🎓 [CERTIFICAT-AUTO] Session trouvée: {SessionTitle}", session.Title);

                // Générer un numéro de certificat unique
                var certificateNumber = await GenerateUniqueCertificateNumberAsync(context);
                _logger.LogInformation("🎓 [CERTIFICAT-AUTO] Numéro de certificat généré: {CertificateNumber}", certificateNumber);

                // Créer le certificat en base de données
                var certificate = new Certificate
                {
                    Title = $"Certificat de réussite - {session.Title}",
                    UserId = userId,
                    SessionId = sessionId,
                    DateGenerated = DateTime.UtcNow,
                    DateDelivered = DateTime.UtcNow,
                    Status = "Generated",
                    CertificateNumber = certificateNumber
                };

                context.Certificates.Add(certificate);
                var changesSaved = await context.SaveChangesAsync();

                _logger.LogInformation(
                    "🎓 [CERTIFICAT-AUTO] 🎉 Certificat généré et sauvegardé ({Changes} changements) - User: {UserId}, Session: {SessionId}, Numéro: {CertificateNumber}, Score: {Score}%",
                    changesSaved, userId, sessionId, certificateNumber, eligibilityResult.SessionScorePercentage.ToString("F1"));

                // Optionnel : Générer le fichier PDF immédiatement
                await GenerateCertificateFileAsync(certificate);

                _logger.LogInformation("🎓 [CERTIFICAT-AUTO] FIN - Traitement réussi");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "🎓 [CERTIFICAT-AUTO] ❌ ERREUR lors de la génération automatique de certificat pour l'utilisateur {UserId} et la session {SessionId}", userId, sessionId);
            }
        }

        private async Task<string> GenerateUniqueCertificateNumberAsync(ApplicationDbContext context)
        {
            string certificateNumber;
            bool exists;

            do
            {
                // Format: CERT-YYYYMMDD-XXXXX (où XXXXX est un nombre aléatoire)
                var datePart = DateTime.Now.ToString("yyyyMMdd");
                var randomPart = new Random().Next(10000, 99999);
                certificateNumber = $"CERT-{datePart}-{randomPart}";

                exists = await context.Certificates
                    .AnyAsync(c => c.CertificateNumber == certificateNumber);
            } while (exists);

            return certificateNumber;
        }

        private async Task GenerateCertificateFileAsync(Certificate certificate)
        {
            try
            {
                _logger.LogInformation("🎓 [CERTIFICAT-AUTO] Génération du fichier PDF pour le certificat {CertificateNumber}", certificate.CertificateNumber);

                // Générer le fichier PDF du certificat
                var pdfData = await _certificateService.GenerateCertificateAsync(
                    certificate.SessionId,
                    certificate.UserId,
                    CertificateType.Pdf);

                // Optionnel : Sauvegarder le fichier sur le serveur
                var fileName = $"certificate_{certificate.CertificateNumber}.pdf";
                var filePath = Path.Combine("wwwroot", "certificates", fileName);

                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                await File.WriteAllBytesAsync(filePath, pdfData);

                // Mettre à jour le chemin du fichier en base
                using var context = await _contextFactory.CreateDbContextAsync();
                var cert = await context.Certificates.FindAsync(certificate.Id);
                if (cert != null)
                {
                    cert.FilePath = $"/certificates/{fileName}";
                    await context.SaveChangesAsync();
                }

                _logger.LogInformation("🎓 [CERTIFICAT-AUTO] ✅ Fichier certificat généré: {FilePath}", filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "🎓 [CERTIFICAT-AUTO] ❌ Erreur lors de la génération du fichier certificat pour {CertificateNumber}", certificate.CertificateNumber);
            }
        }
    }

    /// <summary>
    /// Résultat de la vérification d'éligibilité au certificat
    /// </summary>
    public class CertificateEligibilityResult
    {
        public int SessionId { get; set; }
        public string UserId { get; set; } = string.Empty;
        public bool IsSessionCompleted { get; set; }
        public double SessionScorePercentage { get; set; }
        public bool HasMinimumScore { get; set; }
        public bool IsEligible { get; set; }
        public bool HasExistingCertificate { get; set; }
        
        public string GetStatusMessage()
        {
            if (HasExistingCertificate)
                return "Certificat déjà généré";
            
            if (!IsSessionCompleted)
                return "Session non terminée";
            
            if (!HasMinimumScore)
                return $"Score insuffisant ({SessionScorePercentage:F1}% < 70%)";
            
            if (IsEligible)
                return "Éligible au certificat";
            
            return "Non éligible";
        }
    }
}