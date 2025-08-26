using Microsoft.EntityFrameworkCore;
using Mooc.Data;

namespace Mooc.Services
{
    public interface IAutomaticCertificateService
    {
        Task CheckAndGenerateCertificateAsync(string userId, int sessionId);
        Task<bool> IsSessionCompletedByUserAsync(string userId, int sessionId);
    }

    public class AutomaticCertificateService : IAutomaticCertificateService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private readonly ICertificateGenerationService _certificateService;
        private readonly ILogger<AutomaticCertificateService> _logger;

        public AutomaticCertificateService(
            IDbContextFactory<ApplicationDbContext> contextFactory,
            ICertificateGenerationService certificateService,
            ILogger<AutomaticCertificateService> logger)
        {
            _contextFactory = contextFactory;
            _certificateService = certificateService;
            _logger = logger;
        }

        public async Task<bool> IsSessionCompletedByUserAsync(string userId, int sessionId)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();

                // Récupérer tous les cours obligatoires de la session
                var requiredCourses = await context.Courses
                    .Where(c => c.SessionId == sessionId && c.IsPublished)
                    .Select(c => c.Id)
                    .ToListAsync();

                if (!requiredCourses.Any())
                {
                    _logger.LogInformation("Aucun cours obligatoire trouvé pour la session {SessionId}", sessionId);
                    return false;
                }

                // Vérifier que tous les cours obligatoires sont complétés
                var completedRequiredCourses = await context.CourseProgresses
                    .Where(cp => cp.UserId == userId &&
                                requiredCourses.Contains(cp.CoursId) &&
                                cp.IsCompleted)
                    .CountAsync();

                var isCompleted = completedRequiredCourses == requiredCourses.Count;

                _logger.LogInformation(
                    "Session {SessionId} pour l'utilisateur {UserId}: {CompletedCount}/{TotalCount} cours complétés. Session terminée: {IsCompleted}",
                    sessionId, userId, completedRequiredCourses, requiredCourses.Count, isCompleted);

                return isCompleted;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la vérification de completion de session {SessionId} pour l'utilisateur {UserId}", sessionId, userId);
                return false;
            }
        }

        public async Task CheckAndGenerateCertificateAsync(string userId, int sessionId)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();

                // Vérifier si un certificat existe déjà
                var existingCertificate = await context.Certificates
                    .FirstOrDefaultAsync(c => c.UserId == userId && c.SessionId == sessionId);

                if (existingCertificate != null)
                {
                    _logger.LogInformation("Certificat déjà existant pour l'utilisateur {UserId} et la session {SessionId}", userId, sessionId);
                    return;
                }

                // Vérifier si la session est complétée
                var isSessionCompleted = await IsSessionCompletedByUserAsync(userId, sessionId);

                if (!isSessionCompleted)
                {
                    _logger.LogInformation("Session {SessionId} non complétée pour l'utilisateur {UserId}", sessionId, userId);
                    return;
                }

                // Récupérer les informations de la session
                var session = await context.Sessions
                    .FirstOrDefaultAsync(s => s.Id == sessionId);

                if (session == null)
                {
                    _logger.LogError("Session {SessionId} non trouvée", sessionId);
                    return;
                }

                // Générer un numéro de certificat unique
                var certificateNumber = await GenerateUniqueCertificateNumberAsync(context);

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
                await context.SaveChangesAsync();

                _logger.LogInformation(
                    "Certificat généré automatiquement pour l'utilisateur {UserId} et la session {SessionId}. Numéro: {CertificateNumber}",
                    userId, sessionId, certificateNumber);

                // Optionnel : Générer le fichier PDF immédiatement
                await GenerateCertificateFileAsync(certificate);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la génération automatique de certificat pour l'utilisateur {UserId} et la session {SessionId}", userId, sessionId);
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

                _logger.LogInformation("Fichier certificat généré: {FilePath}", filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la génération du fichier certificat pour {CertificateNumber}", certificate.CertificateNumber);
            }
        }
    }
}