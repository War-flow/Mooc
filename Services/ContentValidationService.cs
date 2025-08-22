using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace Mooc.Services
{
    public interface IContentValidationService
    {
        Task<ValidationResult> ValidateHtmlContentAsync(string htmlContent, string? csrfToken = null);
        Task<ValidationResult> ValidateUrlAsync(string url);
        Task<ValidationResult> ValidateFileAsync(IFormFile file);
        string SanitizeHtmlContent(string htmlContent);
    }

    public class ContentValidationService : IContentValidationService
    {
        private readonly ILogger<ContentValidationService> _logger;
        private readonly IConfiguration _configuration;

        // Configuration des limites
        private const int MAX_CONTENT_LENGTH = 100000; // 100KB
        private const int MAX_FILE_SIZE = 5 * 1024 * 1024; // 5MB
        private const int MAX_URL_LENGTH = 2048;

        // Balises HTML autorisées
        private readonly string[] _allowedTags = 
        {
            "p", "br", "strong", "em", "u", "a", "ul", "ol", "li", "img", 
            "h1", "h2", "h3", "h4", "h5", "h6", "div", "span", "table", 
            "thead", "tbody", "tr", "th", "td", "video", "audio", "iframe"
        };

        // Attributs autorisés par balise
        private readonly Dictionary<string, string[]> _allowedAttributes = new()
        {
            ["a"] = new[] { "href", "title", "rel" },
            ["img"] = new[] { "src", "alt", "width", "height", "loading" },
            ["video"] = new[] { "src", "controls", "preload", "width", "height" },
            ["audio"] = new[] { "src", "controls", "preload" },
            ["iframe"] = new[] { "src", "width", "height", "frameborder", "allowfullscreen" },
            ["table"] = new[] { "class", "style" },
            ["th"] = new[] { "style", "class" },
            ["td"] = new[] { "style", "class" },
            ["div"] = new[] { "class", "style", "data-video-element" }
        };

        // Protocoles autorisés pour les URLs
        private readonly string[] _allowedProtocols = { "https", "http" };

        // Domaines en liste noire
        private readonly string[] _blacklistedDomains = 
        {
            "malicious.com", "phishing.net", "spam.example"
        };

        // Extensions de fichiers autorisées
        private readonly string[] _allowedFileExtensions = 
        {
            ".pdf", ".doc", ".docx", ".txt", ".zip", ".xlsx", ".pptx", ".xls"
        };

        public ContentValidationService(
            ILogger<ContentValidationService> logger, 
            IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        public async Task<ValidationResult> ValidateHtmlContentAsync(string htmlContent, string? csrfToken = null)
        {
            try
            {
                // Validation de base
                if (string.IsNullOrEmpty(htmlContent))
                {
                    return ValidationResult.Success();
                }

                // Vérifier la taille du contenu
                if (htmlContent.Length > MAX_CONTENT_LENGTH)
                {
                    _logger.LogWarning("Contenu trop volumineux: {Length} caractères", htmlContent.Length);
                    return ValidationResult.Failure("Le contenu est trop volumineux (max: 100KB)");
                }

                // Validation CSRF token si fourni
                if (!string.IsNullOrEmpty(csrfToken))
                {
                    var isValidToken = await ValidateCsrfTokenAsync(csrfToken);
                    if (!isValidToken)
                    {
                        _logger.LogWarning("Token CSRF invalide");
                        return ValidationResult.Failure("Token de sécurité invalide");
                    }
                }

                // Valider le HTML avec HtmlAgilityPack
                var doc = new HtmlDocument();
                doc.LoadHtml(htmlContent);

                if (doc.ParseErrors.Any())
                {
                    _logger.LogWarning("Erreurs de parsing HTML détectées");
                    return ValidationResult.Failure("Format HTML invalide");
                }

                // Valider les balises et attributs
                var validationErrors = new List<string>();
                ValidateHtmlNodes(doc.DocumentNode, validationErrors);

                if (validationErrors.Any())
                {
                    _logger.LogWarning("Contenu HTML non conforme: {Errors}", string.Join(", ", validationErrors));
                    return ValidationResult.Failure($"Contenu non autorisé: {string.Join(", ", validationErrors)}");
                }

                // Valider les URLs dans le contenu
                await ValidateUrlsInContentAsync(doc, validationErrors);

                if (validationErrors.Any())
                {
                    return ValidationResult.Failure($"URLs non valides: {string.Join(", ", validationErrors)}");
                }

                return ValidationResult.Success();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la validation du contenu HTML");
                return ValidationResult.Failure("Erreur de validation interne");
            }
        }

        public async Task<ValidationResult> ValidateUrlAsync(string url)
        {
            try
            {
                if (string.IsNullOrEmpty(url))
                {
                    return ValidationResult.Failure("URL vide");
                }

                if (url.Length > MAX_URL_LENGTH)
                {
                    return ValidationResult.Failure("URL trop longue");
                }

                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    return ValidationResult.Failure("Format d'URL invalide");
                }

                // Vérifier le protocole
                if (!_allowedProtocols.Contains(uri.Scheme.ToLower()))
                {
                    _logger.LogWarning("Protocole non autorisé: {Scheme}", uri.Scheme);
                    return ValidationResult.Failure($"Protocole non autorisé: {uri.Scheme}");
                }

                // Vérifier la liste noire des domaines
                var hostname = uri.Host.ToLower();
                if (_blacklistedDomains.Any(domain => hostname.Contains(domain)))
                {
                    _logger.LogWarning("Domaine en liste noire: {Hostname}", hostname);
                    return ValidationResult.Failure("Domaine non autorisé");
                }

                // Validation additionnelle pour les plateformes vidéo
                if (!IsAllowedVideoPlatform(hostname))
                {
                    // Vérifier si c'est un fichier multimédia direct
                    var extension = Path.GetExtension(uri.AbsolutePath).ToLower();
                    var allowedMediaExtensions = new[] { ".mp4", ".webm", ".ogg", ".mp3", ".wav" };
                    
                    if (!allowedMediaExtensions.Contains(extension))
                    {
                        return ValidationResult.Failure("Plateforme ou format de média non autorisé");
                    }
                }

                return ValidationResult.Success();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la validation de l'URL: {Url}", url);
                return ValidationResult.Failure("Erreur de validation de l'URL");
            }
        }

        public async Task<ValidationResult> ValidateFileAsync(IFormFile file)
        {
            try
            {
                if (file == null || file.Length == 0)
                {
                    return ValidationResult.Failure("Fichier vide ou invalide");
                }

                // Vérifier la taille
                if (file.Length > MAX_FILE_SIZE)
                {
                    return ValidationResult.Failure($"Fichier trop volumineux (max: {MAX_FILE_SIZE / (1024 * 1024)}MB)");
                }

                // Vérifier l'extension
                var extension = Path.GetExtension(file.FileName).ToLower();
                if (!_allowedFileExtensions.Contains(extension))
                {
                    _logger.LogWarning("Extension de fichier non autorisée: {Extension}", extension);
                    return ValidationResult.Failure($"Type de fichier non autorisé: {extension}");
                }

                // Vérifier le type MIME
                var allowedMimeTypes = GetAllowedMimeTypes();
                if (!allowedMimeTypes.Contains(file.ContentType.ToLower()))
                {
                    _logger.LogWarning("Type MIME non autorisé: {ContentType}", file.ContentType);
                    return ValidationResult.Failure("Type de fichier non autorisé");
                }

                // Vérification additionnelle du contenu du fichier si nécessaire
                using var stream = file.OpenReadStream();
                var buffer = new byte[512];
                await stream.ReadAsync(buffer, 0, buffer.Length);
                
                // Vérifier les signatures de fichiers pour détecter les faux types
                if (!IsValidFileSignature(buffer, extension))
                {
                    return ValidationResult.Failure("Fichier corrompu ou type invalide");
                }

                return ValidationResult.Success();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la validation du fichier");
                return ValidationResult.Failure("Erreur de validation du fichier");
            }
        }

        public string SanitizeHtmlContent(string htmlContent)
        {
            if (string.IsNullOrEmpty(htmlContent))
                return string.Empty;

            try
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(htmlContent);

                // Supprimer les scripts et styles dangereux
                RemoveDangerousElements(doc);

                // Nettoyer les attributs
                CleanAttributes(doc.DocumentNode);

                // Nettoyer les placeholders
                var cleaned = doc.DocumentNode.OuterHtml
                    .Replace("<div style=\"color: #6c757d; font-style: italic; pointer-events: none;\">Commencez à écrire...</div>", "")
                    .Replace("<div class=\"placeholder-text\" style=\"color: #6c757d; font-style: italic; pointer-events: none;\">", "")
                    .Trim();

                return cleaned;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la sanitisation du contenu HTML");
                return string.Empty;
            }
        }

        // Méthodes privées d'aide
        private void ValidateHtmlNodes(HtmlNode node, List<string> errors)
        {
            if (node.NodeType == HtmlNodeType.Element)
            {
                var tagName = node.Name.ToLower();
                
                if (!_allowedTags.Contains(tagName))
                {
                    errors.Add($"Balise non autorisée: {tagName}");
                    return;
                }

                // Valider les attributs
                if (_allowedAttributes.ContainsKey(tagName))
                {
                    var allowedAttrs = _allowedAttributes[tagName];
                    foreach (var attr in node.Attributes)
                    {
                        if (!allowedAttrs.Contains(attr.Name.ToLower()))
                        {
                            errors.Add($"Attribut non autorisé: {attr.Name} sur {tagName}");
                        }
                    }
                }
            }

            foreach (var child in node.ChildNodes)
            {
                ValidateHtmlNodes(child, errors);
            }
        }

        private async Task ValidateUrlsInContentAsync(HtmlDocument doc, List<string> errors)
        {
            // Valider les liens
            foreach (var link in doc.DocumentNode.SelectNodes("//a[@href]") ?? Enumerable.Empty<HtmlNode>())
            {
                var href = link.GetAttributeValue("href", "");
                if (!string.IsNullOrEmpty(href) && Uri.IsWellFormedUriString(href, UriKind.Absolute))
                {
                    var result = await ValidateUrlAsync(href);
                    if (!result.IsValid)
                    {
                        errors.Add($"URL invalide: {href}");
                    }
                }
            }

            // Valider les images
            foreach (var img in doc.DocumentNode.SelectNodes("//img[@src]") ?? Enumerable.Empty<HtmlNode>())
            {
                var src = img.GetAttributeValue("src", "");
                if (!string.IsNullOrEmpty(src) && Uri.IsWellFormedUriString(src, UriKind.Absolute))
                {
                    var result = await ValidateUrlAsync(src);
                    if (!result.IsValid)
                    {
                        errors.Add($"Image URL invalide: {src}");
                    }
                }
            }
        }

        private bool IsAllowedVideoPlatform(string hostname)
        {
            var allowedPlatforms = new[]
            {
                "youtube.com", "www.youtube.com", "youtu.be", "m.youtube.com",
                "vimeo.com", "www.vimeo.com", "player.vimeo.com",
                "dailymotion.com", "www.dailymotion.com", "dai.ly"
            };

            return allowedPlatforms.Contains(hostname.ToLower());
        }

        private string[] GetAllowedMimeTypes()
        {
            return new[]
            {
                "application/pdf",
                "application/msword",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                "text/plain",
                "application/zip",
                "application/vnd.ms-excel",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "application/vnd.ms-powerpoint",
                "application/vnd.openxmlformats-officedocument.presentationml.presentation"
            };
        }

        private bool IsValidFileSignature(byte[] buffer, string extension)
        {
            // Vérification basique des signatures de fichiers
            return extension switch
            {
                ".pdf" => buffer.Length >= 4 && 
                         buffer[0] == 0x25 && buffer[1] == 0x50 && buffer[2] == 0x44 && buffer[3] == 0x46,
                ".zip" => buffer.Length >= 4 && 
                         buffer[0] == 0x50 && buffer[1] == 0x4B,
                ".txt" => true, // Text files don't have a specific signature
                _ => true // Pour les autres types, accepter pour l'instant
            };
        }

        private void RemoveDangerousElements(HtmlDocument doc)
        {
            var dangerousTags = new[] { "script", "style", "link", "meta", "object", "embed", "form" };
            
            foreach (var tagName in dangerousTags)
            {
                var nodes = doc.DocumentNode.SelectNodes($"//{tagName}");
                if (nodes != null)
                {
                    foreach (var node in nodes.ToList())
                    {
                        node.Remove();
                    }
                }
            }
        }

        private void CleanAttributes(HtmlNode node)
        {
            if (node.NodeType == HtmlNodeType.Element)
            {
                var dangerousAttributes = new[] { "onclick", "onload", "onerror", "onmouseover", "javascript:" };
                
                foreach (var attr in node.Attributes.ToList())
                {
                    if (dangerousAttributes.Any(dangerous => 
                        attr.Name.ToLower().Contains(dangerous) || 
                        attr.Value.ToLower().Contains("javascript:")))
                    {
                        attr.Remove();
                    }
                }
            }

            foreach (var child in node.ChildNodes)
            {
                CleanAttributes(child);
            }
        }

        private async Task<bool> ValidateCsrfTokenAsync(string token)
        {
            // Implémentation basique - à améliorer selon vos besoins
            return !string.IsNullOrEmpty(token) && token.Length > 10;
        }
    }

    public class ValidationResult
    {
        public bool IsValid { get; private set; }
        public string? ErrorMessage { get; private set; }

        private ValidationResult(bool isValid, string? errorMessage = null)
        {
            IsValid = isValid;
            ErrorMessage = errorMessage;
        }

        public static ValidationResult Success() => new(true);
        public static ValidationResult Failure(string errorMessage) => new(false, errorMessage);
    }
}