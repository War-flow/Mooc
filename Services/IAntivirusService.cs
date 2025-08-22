namespace Mooc.Services
{
    public interface IAntivirusService
    {
        Task<AntivirusScanResult> ScanFileAsync(Stream fileStream);
    }

    public class AntivirusScanResult
    {
        public bool IsClean { get; set; }
        public string? Message { get; set; }
    }
}