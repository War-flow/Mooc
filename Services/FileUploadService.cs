using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Hosting;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Mooc.Services
{
    public class FileUploadService
    {
        private readonly IWebHostEnvironment _environment;

        public FileUploadService(IWebHostEnvironment environment)
        {
            _environment = environment;
        }

        public async Task<string> UploadImageAsync(IBrowserFile file)
        {
            try
            {
                // Vérifie que c'est bien une image
                if (!file.ContentType.StartsWith("image/"))
                {
                    throw new InvalidOperationException("Le fichier doit être une image.");
                }

                // Crée le dossier s'il n'existe pas
                var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "sessions");
                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                }

                // Crée un nom de fichier unique
                var uniqueFileName = $"{Guid.NewGuid()}_{file.Name}";
                var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                // Limite la taille du fichier à 2MB
                const long maxFileSize = 2 * 1024 * 1024; // 2MB
                await using var stream = file.OpenReadStream(maxFileSize);
                await using var fileStream = new FileStream(filePath, FileMode.Create);
                await stream.CopyToAsync(fileStream);

                // Retourne le chemin relatif pour l'utilisation dans l'application
                return $"/uploads/sessions/{uniqueFileName}";
            }
            catch (Exception ex)
            {
                // Log error or handle accordingly
                throw new InvalidOperationException($"Erreur lors de l'upload de l'image : {ex.Message}", ex);
            }
        }

        // Méthode pour télécharger un fichier avec rapport de progression
        public async Task<string> UploadFileAsync(Stream fileStream, string contentType, IProgress<double> progress)
        {
            try
            {
                // Déterminer le dossier cible en fonction du type de contenu
                string subfolder = contentType.StartsWith("image/") ? "images" : "files";
                
                // Crée le dossier s'il n'existe pas
                var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", subfolder);
                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                }

                // Crée un nom de fichier unique
                var uniqueFileName = $"{Guid.NewGuid()}.{GetFileExtensionFromContentType(contentType)}";
                var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                // Créer un buffer pour la lecture
                var buffer = new byte[4096];
                var totalRead = 0L;
                var fileLength = fileStream.Length;
                
                // Créer un nouveau fichier pour écrire
                await using var fileWriteStream = new FileStream(filePath, FileMode.Create);
                
                // Lire et écrire par blocs avec rapport de progression
                int bytesRead;
                while ((bytesRead = await fileStream.ReadAsync(buffer)) > 0)
                {
                    await fileWriteStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                    totalRead += bytesRead;
                    progress?.Report((double)totalRead / fileLength * 100);
                }

                // Retourne le chemin relatif pour l'utilisation dans l'application
                return $"/uploads/{subfolder}/{uniqueFileName}";
            }
            catch (Exception ex)
            {
                // Log error or handle accordingly
                throw new InvalidOperationException($"Erreur lors de l'upload du fichier : {ex.Message}", ex);
            }
        }

        public async Task<(bool Success, string? FileUrl, string? ErrorMessage)> UploadFileAsync(Stream fileStream, string fileName)
        {
            try
            {
                // Crée le dossier s'il n'existe pas
                var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "files");
                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                }

                // Crée un nom de fichier unique
                var uniqueFileName = $"{Guid.NewGuid()}_{fileName}";
                var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                // Écrit le fichier sur le disque
                await using var fileWriteStream = new FileStream(filePath, FileMode.Create);
                await fileStream.CopyToAsync(fileWriteStream);

                // Retourne le chemin relatif pour l'utilisation dans l'application
                return (true, $"/uploads/files/{uniqueFileName}", null);
            }
            catch (Exception ex)
            {
                // Log error or handle accordingly
                return (false, null, $"Erreur lors de l'upload du fichier : {ex.Message}");
            }
        }

        public void DeleteImage(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath))
                return;

            try
            {
                // Extraire le nom du fichier du chemin relatif
                string fileName = Path.GetFileName(imagePath);
                
                // Déterminer le dossier en fonction du chemin
                string subfolder = imagePath.Contains("/images/") ? "images" : "sessions";
                
                // Construire le chemin absolu
                var fullPath = Path.Combine(_environment.WebRootPath, "uploads", subfolder, fileName);
                
                // Vérifier si le fichier existe avant de le supprimer
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                }
            }
            catch (Exception ex)
            {
                // Log error or handle accordingly
                throw new InvalidOperationException($"Erreur lors de la suppression de l'image : {ex.Message}", ex);
            }
        }

        public void DeleteFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return;

            try
            {
                // Extraire le nom du fichier du chemin relatif
                string fileName = Path.GetFileName(filePath);
                
                // Construire le chemin absolu
                var fullPath = Path.Combine(_environment.WebRootPath, "uploads", "files", fileName);
                
                // Vérifier si le fichier existe avant de le supprimer
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                }
            }
            catch (Exception ex)
            {
                // Log error or handle accordingly
                throw new InvalidOperationException($"Erreur lors de la suppression du fichier : {ex.Message}", ex);
            }
        }
        
        // Méthode utilitaire pour obtenir l'extension de fichier à partir du type de contenu
        private string GetFileExtensionFromContentType(string contentType)
        {
            return contentType switch
            {
                "image/jpeg" => "jpg",
                "image/png" => "png",
                "image/gif" => "gif",
                "image/svg+xml" => "svg",
                "image/webp" => "webp",
                "image/bmp" => "bmp",
                "image/tiff" => "tiff",
                _ => "bin"
            };
        }
    }
}