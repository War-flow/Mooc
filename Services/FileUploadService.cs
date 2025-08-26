using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using System.Net.Http.Headers;

namespace Mooc.Services
{
    public class FileUploadService
    {
        /// <summary>
        /// Service pour gérer les uploads de fichiers.
        /// </summary>
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<FileUploadService> _logger;
        private readonly IConfiguration _configuration;
        private readonly IContentValidationService _validationService;
        private readonly IAntivirusService _antivirusService; // Injecter le service antivirus

        // Configuration par défaut
        private readonly string[] _allowedImageExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".svg", ".bmp" };
        private readonly string[] _allowedAudioExtensions = { ".mp3", ".wav", ".ogg", ".m4a", ".webm" };
        private readonly string[] _allowedFileExtensions = { ".pdf", ".doc", ".docx", ".txt", ".zip", ".xlsx", ".pptx", ".xls", ".rar" };

        // Tailles maximales par type de fichier
        private readonly Dictionary<string, long> _maxFileSizes = new()
        {
            { "image", 3 * 1024 * 1024 }, // 3MB
            { "audio", 10 * 1024 * 1024 }, // 10MB
            { "file", 50 * 1024 * 1024 } // 50MB
        };

        /// <summary>
        /// Constructeur du service d'upload de fichiers.
        /// </summary>
        public FileUploadService(IWebHostEnvironment environment, ILogger<FileUploadService> logger, IConfiguration configuration, IContentValidationService validationService, IAntivirusService antivirusService)
        {
            _environment = environment;
            _logger = logger;
            _configuration = configuration;
            _validationService = validationService;
            _antivirusService = antivirusService;
        }

        /// <summary>
        /// Upload une image.
        /// </summary>
        public async Task<string> UploadImageAsync(IBrowserFile file)
        {
            _logger.LogInformation("Début de l'upload d'image: {FileName}, Taille: {Size} bytes", file.Name, file.Size);
            
            try
            {
                // ⭐ CORRECTION : Validation de base d'abord
                ValidateFile(file, "image", _allowedImageExtensions, _maxFileSizes["image"]);
                
                // ⭐ CORRECTION : Utiliser une approche en plusieurs étapes pour éviter la corruption
                
                // Étape 1 : Lire le fichier complet en mémoire pour validation
                byte[] fileContent;
                await using (var stream = file.OpenReadStream(_maxFileSizes["image"]))
                {
                    using var memoryStream = new MemoryStream();
                    await stream.CopyToAsync(memoryStream);
                    fileContent = memoryStream.ToArray();
                }
                
                // Étape 2 : Validation de la signature sur les premiers bytes
                if (fileContent.Length < 4)
                {
                    throw new InvalidOperationException("Fichier trop petit pour être une image valide");
                }
                
                if (!IsValidImageSignature(fileContent.Take(4).ToArray(), file.ContentType))
                {
                    throw new InvalidOperationException("Le fichier n'est pas une image valide");
                }
                
                // Étape 3 : Scan antivirus si disponible (sur le contenu en mémoire)
                if (_antivirusService != null)
                {
                    using var validationStream = new MemoryStream(fileContent);
                    var scanResult = await _antivirusService.ScanFileAsync(validationStream);
                    if (!scanResult.IsClean)
                    {
                        throw new InvalidOperationException("Fichier détecté comme malveillant");
                    }
                }
                
                // Étape 4 : Sauvegarde du fichier intact
                var safeFileName = GenerateUniqueFileName(file.Name);
                var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "images");
                EnsureDirectoryExists(uploadsFolder);
                SetSecureDirectoryPermissions(uploadsFolder);
                
                var filePath = Path.Combine(uploadsFolder, safeFileName);
                
                // ⭐ CORRECTION : Écrire directement le contenu intact sans re-lecture du stream
                await File.WriteAllBytesAsync(filePath, fileContent);
                
                var relativePath = $"/uploads/images/{safeFileName}";
                _logger.LogInformation("Upload d'image réussi: {RelativePath}", relativePath);
                
                return relativePath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'upload d'image: {FileName}", file.Name);
                throw new InvalidOperationException($"Erreur lors de l'upload de l'image : {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Upload un audio.
        /// </summary>
        /// <param name="file"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public async Task<string> UploadAudioAsync(IBrowserFile file)
        {
            _logger.LogInformation("Début de l'upload d'audio: {FileName}, Taille: {Size} bytes", file.Name, file.Size);
            
            try
            {
                ValidateFile(file, "audio", _allowedAudioExtensions, _maxFileSizes["audio"]);
                
                var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "audio");
                EnsureDirectoryExists(uploadsFolder);

                var uniqueFileName = GenerateUniqueFileName(file.Name);
                var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                await SaveFileAsync(file, filePath, _maxFileSizes["audio"]);
                
                var relativePath = $"/uploads/audio/{uniqueFileName}";
                _logger.LogInformation("Upload d'audio réussi: {RelativePath}", relativePath);
                
                return relativePath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'upload d'audio: {FileName}", file.Name);
                throw new InvalidOperationException($"Erreur lors de l'upload de l'audio : {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Upload un fichier générique.
        /// </summary>
        /// <param name="file"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public async Task<string> UploadFileAsync(IBrowserFile file)
        {
            _logger.LogInformation("Début de l'upload de fichier: {FileName}, Taille: {Size} bytes", file.Name, file.Size);
            
            try
            {
                // ⭐ CORRECTION : Validation directe sans conversion IFormFile problématique
                ValidateFile(file, "file", _allowedFileExtensions, _maxFileSizes["file"]);
                
                // ⭐ CORRECTION : Validation personnalisée pour IBrowserFile
                var validationResult = await ValidateBrowserFileAsync(file);
                if (!validationResult.IsValid)
                {
                    _logger.LogWarning("Fichier rejeté: {Error}", validationResult.ErrorMessage);
                    throw new InvalidOperationException(validationResult.ErrorMessage ?? "Fichier invalide");
                }
                
                var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "files");
                EnsureDirectoryExists(uploadsFolder);

                var uniqueFileName = GenerateUniqueFileName(file.Name);
                var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                await SaveFileAsync(file, filePath, _maxFileSizes["file"]);
                
                var relativePath = $"/uploads/files/{uniqueFileName}";
                _logger.LogInformation("Upload de fichier réussi: {RelativePath}", relativePath);
                
                return relativePath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'upload de fichier: {FileName}", file.Name);
                throw new InvalidOperationException($"Erreur lors de l'upload du fichier : {ex.Message}", ex);
            }
        }

        // ⭐ NOUVELLE MÉTHODE : Validation directe pour IBrowserFile
        private async Task<ValidationResult> ValidateBrowserFileAsync(IBrowserFile file)
        {
            try
            {
                if (file == null || file.Size == 0)
                {
                    return ValidationResult.Failure("Fichier vide ou invalide");
                }

                // Vérifier l'extension
                var extension = Path.GetExtension(file.Name).ToLower();
                var allowedExtensions = new[] { ".pdf", ".doc", ".docx", ".txt", ".zip", ".xlsx", ".pptx", ".xls", ".rar" };
                
                if (!allowedExtensions.Contains(extension))
                {
                    return ValidationResult.Failure($"Type de fichier non autorisé: {extension}");
                }

                // Vérifier le type MIME
                var allowedMimeTypes = new[]
                {
                    "application/pdf",
                    "application/msword",
                    "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                    "text/plain",
                    "application/zip",
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    "application/vnd.ms-excel",
                    "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                    "application/x-rar-compressed"
                };
                
                if (!allowedMimeTypes.Contains(file.ContentType?.ToLower()))
                {
                    return ValidationResult.Failure("Type de fichier non autorisé");
                }

                // Validation de signature simplifiée pour IBrowserFile
                try
                {
                    // ⭐ CORRECTION : Utiliser la taille maximale autorisée pour ce type de fichier au lieu de 1024
                    using var stream = file.OpenReadStream(_maxFileSizes["file"]); // 50MB au lieu de 1024 bytes
                    var buffer = new byte[512];
                    var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    
                    if (bytesRead > 0)
                    {
                        Array.Resize(ref buffer, bytesRead);
                        
                        if (!IsValidFileSignatureForBrowser(buffer, extension))
                        {
                            return ValidationResult.Failure("Fichier corrompu ou type invalide");
                        }
                    }
                }
                catch (NotSupportedException)
                {
                    // Si la lecture de signature échoue, accepter basé sur l'extension et MIME
                    _logger.LogDebug("Validation de signature ignorée pour IBrowserFile: {FileName}", file.Name);
                }

                return ValidationResult.Success();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la validation de IBrowserFile");
                return ValidationResult.Failure("Erreur de validation du fichier");
            }
        }

        private bool IsValidFileSignatureForBrowser(byte[] buffer, string extension)
        {
            if (buffer.Length < 4) return false;
            
            // Signatures simplifiées pour validation browser
            return extension switch
            {
                ".pdf" => buffer.Length >= 4 && buffer[0] == 0x25 && buffer[1] == 0x50 && buffer[2] == 0x44 && buffer[3] == 0x46, // %PDF
                ".docx" or ".xlsx" or ".pptx" => buffer.Length >= 4 && buffer[0] == 0x50 && buffer[1] == 0x4B && buffer[2] == 0x03 && buffer[3] == 0x04, // ZIP
                ".doc" or ".xls" => buffer.Length >= 4 && buffer[0] == 0xD0 && buffer[1] == 0xCF && buffer[2] == 0x11 && buffer[3] == 0xE0, // OLE
                ".zip" or ".rar" => buffer.Length >= 4 && buffer[0] == 0x50 && buffer[1] == 0x4B && buffer[2] == 0x03 && buffer[3] == 0x04, // ZIP
                ".txt" => true, // Texte peut avoir n'importe quelle signature
                _ => true // Pour les autres types, accepter
            };
        }

        /// <summary>
        /// Supprime un fichier du serveur.
        
        public void DeleteFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return;

            try
            {
                var fileName = Path.GetFileName(filePath);
                var subfolder = DetermineSubfolder(filePath);
                var fullPath = Path.Combine(_environment.WebRootPath, "uploads", subfolder, fileName);
                
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                    _logger.LogInformation("Fichier supprimé: {FilePath}", fullPath);
                }
                else
                {
                    _logger.LogWarning("Tentative de suppression d'un fichier inexistant: {FilePath}", fullPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la suppression du fichier: {FilePath}", filePath);
                throw new InvalidOperationException($"Erreur lors de la suppression du fichier : {ex.Message}", ex);
            }
        }

        private static string DetermineSubfolder(string filePath)
        {
            if (filePath.Contains("/images/")) return "images";
            if (filePath.Contains("/audio/")) return "audio";
            if (filePath.Contains("/files/")) return "files";
            if (filePath.Contains("/sessions/")) return "sessions";
            return "files"; // Par défaut
        }

        private void ValidateFile(IBrowserFile file, string fileType, string[] allowedExtensions, long maxSize)
        {
            if (file == null)
                throw new ArgumentNullException(nameof(file));

            if (file.Size <= 0)
                throw new InvalidOperationException("Le fichier est vide.");

            if (file.Size > maxSize)
                throw new InvalidOperationException($"Le fichier est trop volumineux. Taille maximale autorisée : {maxSize / (1024 * 1024)} MB.");

            var extension = Path.GetExtension(file.Name).ToLowerInvariant();
            if (string.IsNullOrEmpty(extension) || !allowedExtensions.Contains(extension))
            {
                throw new InvalidOperationException($"Type de fichier non autorisé. Extensions acceptées pour {fileType} : {string.Join(", ", allowedExtensions)}");
            }

            // Validation supplémentaire du nom de fichier
            if (string.IsNullOrWhiteSpace(file.Name) || file.Name.Length > 255)
                throw new InvalidOperationException("Nom de fichier invalide.");
        }

        private string GenerateUniqueFileName(string originalFileName)
        {
            var sanitizedName = SanitizeFileName(originalFileName);
            return $"{Guid.NewGuid()}_{sanitizedName}";
        }

        private static string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return "file";

            // Supprimer les caractères non autorisés
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
            
            // Remplacer les espaces par des underscores et limiter la longueur
            sanitized = Regex.Replace(sanitized, @"\s+", "_");
            sanitized = sanitized.Length > 100 ? sanitized[..100] : sanitized;
            
            return string.IsNullOrEmpty(sanitized) ? "file" : sanitized;
        }

        private void EnsureDirectoryExists(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
                _logger.LogInformation("Dossier créé: {DirectoryPath}", directoryPath);
            }
        }

        private async Task SaveFileAsync(IBrowserFile file, string filePath, long maxSize)
        {
            await using var stream = file.OpenReadStream(maxSize);
            await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true);
            await stream.CopyToAsync(fileStream);
        }
        // <summary>
        /// Supprime un fichier du serveur.

        private bool IsValidImageSignature(byte[] buffer, string contentType)
        {
            // Vérifier les magic bytes des formats d'images
            return contentType switch
            {
                "image/jpeg" => buffer[0] == 0xFF && buffer[1] == 0xD8,
                "image/png" => buffer[0] == 0x89 && buffer[1] == 0x50 && buffer[2] == 0x4E && buffer[3] == 0x47,
                "image/gif" => (buffer[0] == 0x47 && buffer[1] == 0x49 && buffer[2] == 0x46),
                _ => false
            };
        }

        /// <summary>
        /// Définit des permissions sécurisées sur le dossier spécifié.
        /// Sous Windows, cette méthode ne fait rien.
        /// Sous Linux, elle définit les permissions à 750.
        /// </summary>
        private void SetSecureDirectoryPermissions(string directoryPath)
        {
            try
            {
                if (!Directory.Exists(directoryPath))
                    return;

                if (OperatingSystem.IsLinux())
                {
                    // Nécessite System.Runtime.InteropServices et System.Diagnostics
                    var chmod = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "/bin/chmod",
                        Arguments = "750 " + directoryPath,
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var process = System.Diagnostics.Process.Start(chmod);
                    process?.WaitForExit();
                }
                // Sous Windows, on pourrait utiliser DirectorySecurity si besoin
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Impossible de définir les permissions sécurisées sur le dossier: {DirectoryPath}", directoryPath);
            }
        }
    }
}