using Mooc.Services;

namespace Mooc.Services
{
    public class NoOpAntivirusService : IAntivirusService
    {
        private readonly ILogger<NoOpAntivirusService> _logger;

        public NoOpAntivirusService(ILogger<NoOpAntivirusService> logger)
        {
            _logger = logger;
        }

        public Task<AntivirusScanResult> ScanFileAsync(Stream fileStream)
        {
            _logger.LogInformation("Scan antivirus désactivé - retour d'un résultat propre");
            return Task.FromResult(new AntivirusScanResult { IsClean = true, Message = "Scan désactivé" });
        }
    }
}