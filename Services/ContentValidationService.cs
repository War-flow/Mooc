using System.Text.RegularExpressions;
using System.Text; // Ajouté pour StringBuilder
using HtmlAgilityPack;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http;
using Ganss.Xss;
using AngleSharp.Css.Dom;

namespace Mooc.Services
{
    public interface IContentValidationService
    {
        Task<ValidationResult> ValidateHtmlContentAsync(string htmlContent, string? csrfToken = null);
        Task<ValidationResult> ValidateUrlAsync(string url, bool isImageUrl);
        Task<ValidationResult> ValidateFileAsync(IFormFile file);
        string SanitizeHtmlContent(string htmlContent, SanitizationLevel level = SanitizationLevel.Minimal);
    }

    public class ContentValidationSettings
    {
        public int MaxContentLength { get; set; } = 100000;
        public int MaxFileSize { get; set; } = 5 * 1024 * 1024;
        public int MaxUrlLength { get; set; } = 2048;
        
        // Ajouter toutes les balises nécessaires pour l'éditeur riche
        public string[] AllowedTags { get; set; } = new[]
        {
            "p", "br", "strong", "em", "u", "a", "ul", "ol", "li", "img", 
            "h1", "h2", "h3", "h4", "h5", "h6", "b", "i", "div", "iframe", 
            "font", "span", "table", "thead", "tbody", "tr", "td", "th", 
            "video", "audio", "source", "strike", "blockquote", "pre", "code"
        };
        
        // Ajouter les attributs autorisés par balise
        public Dictionary<string, string[]> AllowedAttributes { get; set; } = new()
        {
            ["a"] = new[] { "href", "title", "target", "rel", "class", "style" },
            ["img"] = new[] { "src", "alt", "width", "height", "loading", "style", "class" },
            ["iframe"] = new[] { "src", "width", "height", "frameborder", "allowfullscreen", "style", "class", "title", "allow" },
            ["video"] = new[] { "src", "controls", "width", "height", "preload", "style", "class", "poster", "autoplay", "loop", "muted" },
            ["audio"] = new[] { "src", "controls", "preload", "style", "class", "autoplay", "loop", "muted" },
            ["source"] = new[] { "src" , "type" },
            ["div"] = new[] { "class", "style", "data-video-element", "data-preserved-video", "align", "id" },
            ["span"] = new[] { "class", "style" },
            ["table"] = new[] { "class", "style", "border", "cellpadding", "cellspacing" },
            ["td"] = new[] { "style", "contenteditable", "class", "colspan", "rowspan" },
            ["th"] = new[] { "style", "contenteditable", "class", "colspan", "rowspan" },
            ["p"] = new[] { "class", "style", "align" },
            ["font"] = new[] { "color", "size", "face", "style" }, // Ajouter 'style'
            ["blockquote"] = new[] { "class", "style", "cite" },
            ["strong"] = new[] { "class", "style" },
            ["em"] = new[] { "class", "style" },
            ["b"] = new[] { "class", "style" },
            ["i"] = new[] { "class", "style" },
            ["u"] = new[] { "class", "style" },
            ["strike"] = new[] { "class", "style" },
            // Ajouter des balises pour les couleurs
            ["h1"] = new[] { "class", "style", "align" },
            ["h2"] = new[] { "class", "style", "align" },
            ["h3"] = new[] { "class", "style", "align" },
            ["h4"] = new[] { "class", "style", "align" },
            ["h5"] = new[] { "class", "style", "align" },
            ["h6"] = new[] { "class", "style", "align" }
        };
        
        public string[] AllowedProtocols { get; set; } = new[] { "http", "https", "mailto", "tel", "data" };
        public string[] BlacklistedDomains { get; set; } = new[] { "malicious.com", "phishing.net" };
        public string[] AllowedFileExtensions { get; set; } = new[] { ".pdf", ".doc", ".docx", ".txt", ".zip", ".xlsx", ".xls", ".pptx" };
        public string[] AllowedVideoPlatforms { get; set; } = new[] { 
            "youtube.com", "www.youtube.com", "youtu.be", "m.youtube.com",
            "vimeo.com", "player.vimeo.com", "www.vimeo.com",
            "dailymotion.com", "www.dailymotion.com", "dai.ly"
        };
    }

    public class ContentValidationService : IContentValidationService
    {
        private readonly ILogger<ContentValidationService> _logger;
        private readonly ContentValidationSettings _settings;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMemoryCache _cache;

        public ContentValidationService(
            ILogger<ContentValidationService> logger,
            IOptions<ContentValidationSettings> settings,
            IHttpClientFactory httpClientFactory,
            IMemoryCache cache)
        {
            _logger = logger;
            _settings = settings.Value;
            _httpClientFactory = httpClientFactory;
            _cache = cache;
        }

        public async Task<ValidationResult> ValidateHtmlContentAsync(string htmlContent, string? csrfToken = null)
        {
            try
            {
                // Utiliser un StringBuilder pour collecter les erreurs
                var errors = new StringBuilder();

                // Validation de base
                if (string.IsNullOrEmpty(htmlContent))
                {
                    return ValidationResult.Success();
                }

                // Vérifier la taille du contenu
                if (htmlContent.Length > _settings.MaxContentLength)
                {
                    _logger.LogWarning("Contenu trop volumineux: {Length} caractères", htmlContent.Length);
                    return ValidationResult.Failure($"Le contenu est trop volumineux (max: {_settings.MaxContentLength / 1024}KB)");
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
                    var parseErrors = string.Join(", ", doc.ParseErrors.Select(e => $"Ligne {e.Line}: {e.Reason}"));
                    _logger.LogWarning("Erreurs de parsing HTML détectées: {Errors}", parseErrors);
                    return ValidationResult.Failure($"Format HTML invalide: {parseErrors}");
                }

                // Valider la structure et les balises
                var validationErrors = new List<string>();
                var validationContext = new ValidationContext
                {
                    MaxDepth = 20,
                    CurrentDepth = 0,
                    NodeCount = 0,
                    MaxNodes = 1000
                };

                ValidateHtmlNodesWithContext(doc.DocumentNode, validationErrors, validationContext);

                if (validationErrors.Any())
                {
                    _logger.LogWarning("Contenu HTML non conforme: {Errors}", string.Join(", ", validationErrors));
                    return ValidationResult.Failure($"Contenu non autorisé: {string.Join(", ", validationErrors)}");
                }

                // Valider les URLs dans le contenu (avec parallélisation)
                await ValidateUrlsInContentParallelAsync(doc, validationErrors);

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

        private class ValidationContext
        {
            public int MaxDepth { get; set; }
            public int CurrentDepth { get; set; }
            public int NodeCount { get; set; }
            public int MaxNodes { get; set; }
        }

        private void ValidateHtmlNodesWithContext(HtmlNode node, List<string> errors, ValidationContext context)
        {
            if (context.NodeCount++ > context.MaxNodes)
            {
                errors.Add("Document HTML trop complexe");
                return;
            }

            if (context.CurrentDepth > context.MaxDepth)
            {
                errors.Add("Imbrication HTML trop profonde");
                return;
            }

            context.CurrentDepth++;

            // Validation existante...
            if (node.NodeType == HtmlNodeType.Element)
            {
                var tagName = node.Name.ToLower();
                              
                if (!_settings.AllowedTags.Contains(tagName))
                {
                    errors.Add($"Balise non autorisée: <{tagName}>");
                }
                else if (_settings.AllowedAttributes.TryGetValue(tagName, out var allowedAttrs))
                {
                    foreach (var attr in node.Attributes.ToList())
                    {
                        var attrNameLower = attr.Name.ToLower();
                        // Autoriser les attributs data-* pour flexibilité
                        if (!attrNameLower.StartsWith("data-") && !allowedAttrs.Contains(attrNameLower))
                        {
                            // Ajouter une vérification de débogage
                            _logger.LogDebug("Validation attribut: '{AttrName}' (minuscule: '{AttrNameLower}') sur <{TagName}>. Attributs autorisés: [{AllowedAttrs}]", 
                                attr.Name, attrNameLower, tagName, string.Join(", ", allowedAttrs));
                    
                            errors.Add($"Attribut non autorisé '{attr.Name}' sur <{tagName}>");
                        }
                    }
                }
                else
                {
                    // Si aucune configuration d'attribut trouvée pour cette balise, ajouter du débogage
                    _logger.LogDebug("Aucune configuration d'attribut trouvée pour la balise <{TagName}>", tagName);
                }
            }

            foreach (var child in node.ChildNodes)
            {
                ValidateHtmlNodesWithContext(child, errors, context);
            }

            context.CurrentDepth--;
        }

        private async Task ValidateUrlsInContentParallelAsync(HtmlDocument doc, List<string> errors)
        {
            var urlValidationTasks = new List<Task<(string url, ValidationResult result)>>();

            // Collecter toutes les URLs
            var urlsToValidate = new HashSet<string>();

            // Ajouter les liens
            var links = doc.DocumentNode.SelectNodes("//a[@href]");
            if (links != null)
            {
                foreach (var link in links)
                {
                    var href = link.GetAttributeValue("href", "");
                    if (!string.IsNullOrEmpty(href) && IsValidUrlForValidation(href))
                    {
                        urlsToValidate.Add(href);
                    }
                }
            }

            // Ajouter les images, vidéos, etc.
            var mediaSources = doc.DocumentNode.SelectNodes("//img[@src] | //video[@src] | //audio[@src] | //iframe[@src] | //source[@src]");
            if (mediaSources != null)
            {
                foreach (var media in mediaSources)
                {
                    var src = media.GetAttributeValue("src", "");
                    if (!string.IsNullOrEmpty(src) && IsValidUrlForValidation(src))
                    {
                        urlsToValidate.Add(src);
                    }
                }
            }

            // Valider en parallèle avec limitation de concurrence
            var semaphore = new SemaphoreSlim(5); // Max 5 validations simultanées
            
            foreach (var url in urlsToValidate)
            {
                urlValidationTasks.Add(ValidateUrlWithSemaphoreAsync(url, semaphore));
            }

            var results = await Task.WhenAll(urlValidationTasks);

            foreach (var (url, result) in results)
            {
                if (!result.IsValid)
                {
                    errors.Add($"URL invalide: {url} - {result.ErrorMessage}");
                }
            }
        }

        // ⭐ NOUVELLE MÉTHODE : Vérifier si une URL doit être validée
        private bool IsValidUrlForValidation(string url)
        {
            if (string.IsNullOrEmpty(url))
                return false;

            // ⭐ CORRECTION : Accepter directement les URLs relatives locales
            if (url.StartsWith("/uploads/"))
            {
                _logger.LogDebug("URL locale acceptée: {Url}", url);
                return false; // Ne pas valider, considérer comme valide
            }

            // ⭐ CORRECTION : Accepter les URLs data: pour les images encodées
            if (url.StartsWith("data:"))
            {
                return false; // Ne pas valider, considérer comme valide
            }

            // ⭐ CORRECTION : Vérifier si c'est une URL absolue valide
            try
            {
                return Uri.IsWellFormedUriString(url, UriKind.Absolute);
            }
            catch
            {
                return false;
            }
        }

        private async Task<(string url, ValidationResult result)> ValidateUrlWithSemaphoreAsync(string url, SemaphoreSlim semaphore)
        {
            await semaphore.WaitAsync();
            try
            {
                var isImage = url.Contains("/images/") || Regex.IsMatch(url, @"\.(jpg|jpeg|png|gif|webp|svg)$", RegexOptions.IgnoreCase);
                var result = await ValidateUrlAsync(url, isImage);
                return (url, result);
            }
            finally
            {
                semaphore.Release();
            }
        }

        public async Task<ValidationResult> ValidateUrlAsync(string url, bool isImageUrl)
        {
            try
            {
                // Vérifier le cache
                var cacheKey = $"url_validation_{url}";
                if (_cache.TryGetValue<ValidationResult>(cacheKey, out var cachedResult))
                {
                    return cachedResult;
                }

                // ⭐ CORRECTION : Traitement spécial pour les URLs locales
                if (string.IsNullOrEmpty(url))
                {
                    return ValidationResult.Failure("URL vide");
                }

                if (url.Length > _settings.MaxUrlLength)
                {
                    return ValidationResult.Failure("URL trop longue");
                }

                // ⭐ CORRECTION : Accepter directement les URLs locales
                if (url.StartsWith("/uploads/"))
                {
                    var localResult = ValidationResult.Success();
                    _cache.Set(cacheKey, localResult, TimeSpan.FromHours(24)); // Cache plus long pour les URLs locales
                    _logger.LogDebug("URL locale validée: {Url}", url);
                    return localResult;
                }

                // ⭐ CORRECTION : Accepter les URLs data: 
                if (url.StartsWith("data:"))
                {
                    var dataResult = ValidationResult.Success();
                    _cache.Set(cacheKey, dataResult, TimeSpan.FromHours(1));
                    return dataResult;
                }

                // ⭐ CORRECTION : Gérer les URLs relatives et absolues
                Uri uri;
                if (url.StartsWith("/"))
                {
                    // URL relative - validation basique uniquement
                    if (!Uri.TryCreate(url, UriKind.Relative, out uri))
                    {
                        return ValidationResult.Failure("Format d'URL relative invalide");
                    }
                    
                    var relativeResult = ValidationResult.Success();
                    _cache.Set(cacheKey, relativeResult, TimeSpan.FromHours(1));
                    return relativeResult;
                }
                else
                {
                    // URL absolue
                    if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
                    {
                        return ValidationResult.Failure("Format d'URL invalide");
                    }

                    // Vérifier le protocole
                    if (!_settings.AllowedProtocols.Contains(uri.Scheme.ToLower()))
                    {
                        _logger.LogWarning("Protocole non autorisé: {Scheme}", uri.Scheme);
                        return ValidationResult.Failure($"Protocole non autorisé: {uri.Scheme}");
                    }

                    // Vérifier la liste noire des domaines
                    var hostname = uri.Host.ToLower();
                    if (_settings.BlacklistedDomains.Any(domain => hostname.Contains(domain)))
                    {
                        _logger.LogWarning("Domaine en liste noir: {Hostname}", hostname);
                        return ValidationResult.Failure("Domaine non autorisé");
                    }
                }

                // Vérification HTTP HEAD pour valider l'existence
                var result = await ValidateUrlAccessibilityAsync(uri, isImageUrl);
                
                // Mettre en cache le résultat pour 1 heure
                _cache.Set(cacheKey, result, TimeSpan.FromHours(1));
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la validation de l'URL: {Url}", url);
                return ValidationResult.Failure("Erreur de validation de l'URL");
            }
        }

        private async Task<ValidationResult> ValidateUrlAccessibilityAsync(Uri uri, bool isImageUrl)
        {
            // ⭐ CORRECTION : Améliorer la validation pour les URLs relatives
            if (!uri.IsAbsoluteUri)
            {
                // Pour les URLs relatives (comme /uploads/images/...), considérer comme valides
                return ValidationResult.Success();
            }

            using var httpClient = _httpClientFactory.CreateClient("ValidationClient");
            httpClient.Timeout = TimeSpan.FromSeconds(5);

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Head, uri);
                var response = await httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("URL inaccessible: {Url} - Status: {StatusCode}", uri, response.StatusCode);
                    return ValidationResult.Failure($"URL inaccessible (HTTP {(int)response.StatusCode})");
                }

                // Vérifier le Content-Type pour les images
                if (isImageUrl && response.Content.Headers.ContentType != null)
                {
                    var contentType = response.Content.Headers.ContentType.MediaType?.ToLower();
                    var allowedImageTypes = new[] { "image/jpeg", "image/png", "image/gif", "image/webp", "image/svg+xml" };
                    
                    if (!allowedImageTypes.Contains(contentType))
                    {
                        _logger.LogWarning("Type d'image non autorisé: {ContentType} pour {Url}", contentType, uri);
                        return ValidationResult.Failure("Type d'image non autorisé");
                    }
                }

                return ValidationResult.Success();
            }
            catch (TaskCanceledException)
            {
                _logger.LogWarning("Timeout lors de la validation de l'URL: {Url}", uri);
                return ValidationResult.Failure("URL inaccessible (timeout)");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Erreur HTTP lors de la validation de l'URL: {Url}", uri);
                return ValidationResult.Failure("URL inaccessible");
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
                if (file.Length > _settings.MaxFileSize)
                {
                    return ValidationResult.Failure($"Fichier trop volumineux (max: {_settings.MaxFileSize / (1024 * 1024)}MB)");
                }

                // Vérifier l'extension
                var extension = Path.GetExtension(file.FileName).ToLower();
                if (!_settings.AllowedFileExtensions.Contains(extension))
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

                // ⭐ CORRECTION : Gestion spéciale pour les BrowserFileStream
                byte[] buffer;
                try
                {
                    // Essayer d'abord la lecture directe avec limitation de taille
                    using var stream = file.OpenReadStream();
                    
                    // ⭐ CORRECTION : Lire directement sans manipuler Position
                    buffer = new byte[Math.Min(512, file.Length)];
                    var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    
                    // Ajuster la taille du buffer si moins d'octets lus
                    if (bytesRead < buffer.Length)
                    {
                        Array.Resize(ref buffer, bytesRead);
                    }
                }
                catch (NotSupportedException ex) when (ex.Message.Contains("Position"))
                {
                    // ⭐ CORRECTION : Si BrowserFileStream ne supporte pas Position,
                    // utiliser une approche alternative
                    _logger.LogWarning("BrowserFileStream détecté, utilisation de l'approche alternative pour la validation");
                    
                    try
                    {
                        // Lire tout le début du fichier en une seule fois
                        using var stream = file.OpenReadStream();
                        using var memoryStream = new MemoryStream();
                        
                        // Lire seulement les premiers 512 octets pour la signature
                        var tempBuffer = new byte[4096];
                        var totalRead = 0;
                        
                        while (totalRead < 512)
                        {
                            var remainingToRead = Math.Min(tempBuffer.Length, 512 - totalRead);
                            var bytesRead = await stream.ReadAsync(tempBuffer, 0, remainingToRead);
                            
                            if (bytesRead == 0) break; // Fin de fichier
                            
                            await memoryStream.WriteAsync(tempBuffer, 0, bytesRead);
                            totalRead += bytesRead;
                        }
                        
                        buffer = memoryStream.ToArray();
                    }
                    catch (Exception innerEx)
                    {
                        _logger.LogError(innerEx, "Erreur lors de la lecture alternative du fichier pour validation");
                        // ⭐ CORRECTION : En cas d'échec de lecture, valider seulement l'extension et le MIME
                        _logger.LogInformation("Validation limitée appliquée (extension + MIME) pour le fichier: {FileName}", file.FileName);
                        return ValidationResult.Success();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erreur lors de l'ouverture du flux de fichier pour validation");
                    return ValidationResult.Failure("Erreur lors de la lecture du fichier");
                }
                
                // Vérifier les signatures de fichiers pour détecter les faux types
                if (buffer.Length > 0 && !IsValidFileSignature(buffer, extension))
                {
                    _logger.LogWarning("Signature de fichier invalide pour l'extension {Extension}", extension);
                    return ValidationResult.Failure("Fichier corrompu ou type invalide");
                }

                _logger.LogDebug("Validation de fichier réussie: {FileName}, Taille: {Size} bytes", file.FileName, file.Length);
                return ValidationResult.Success();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la validation du fichier: {FileName}", file?.FileName ?? "unknown");
                return ValidationResult.Failure("Erreur de validation du fichier");
            }
        }

        public string SanitizeHtmlContent(string htmlContent, SanitizationLevel level = SanitizationLevel.Minimal)
        {
            if (string.IsNullOrEmpty(htmlContent))
                return string.Empty;

            switch (level)
            {
                case SanitizationLevel.None:
                    return htmlContent;
                    
                case SanitizationLevel.Minimal:
                    return MinimalSanitization(htmlContent);
                    
                case SanitizationLevel.Standard:
                    return StandardSanitization(htmlContent);
                    
                case SanitizationLevel.Strict:
                    return StrictSanitization(htmlContent);
                    
                default:
                    return htmlContent;
            }
        }

        private string MinimalSanitization(string htmlContent)
        {
            try
            {
                _logger.LogDebug("Début sanitisation minimale. Contenu original longueur: {Length}", htmlContent.Length);
                
                var doc = new HtmlDocument();
                doc.LoadHtml(htmlContent);
                
                // Compter les éléments vidéo avant traitement
                var videoElementsBefore = doc.DocumentNode.SelectNodes("//iframe | //video | //div[@data-video-element]")?.Count ?? 0;
                _logger.LogDebug("Éléments vidéo détectés avant traitement: {Count}", videoElementsBefore);
                
                // CORRECTION : Préserver explicitement les vidéos AVANT tout nettoyage
                PreserveVideoElements(doc);
                
                // Supprimer uniquement les scripts dangereux
                var scripts = doc.DocumentNode.SelectNodes("//script");
                scripts?.ToList().ForEach(s => s.Remove());
                
                var allNodes = doc.DocumentNode.SelectNodes("//*");
                if (allNodes != null)
                {
                    foreach (var node in allNodes)
                    {
                        var tagName = node.Name.ToLower();
                        
                        // CORRECTION : Ne pas traiter les éléments vidéo préservés
                        if (node.GetAttributeValue("data-preserve-video", "") == "true")
                        {
                            continue;
                        }
                        
                        var formattingTags = new[] { "b", "i", "u", "strong", "em", "strike", "font", "span", "p", "div", "h1", "h2", "h3", "h4", "h5", "h6" };
                        
                        if (formattingTags.Contains(tagName))
                        {
                            PreserveColorFormatting(node);
                            continue;
                        }
                        
                        // Supprimer seulement les attributs dangereux pour les autres éléments
                        var eventAttributes = node.Attributes
                            .Where(a => a.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase))
                            .ToList();
                        
                        foreach (var attr in eventAttributes)
                        {
                            attr.Remove();
                        }
                        
                        // Nettoyer les URLs JavaScript SAUF pour les vidéos préservées
                        var urlAttrs = node.Attributes.Where(a => 
                            (a.Name.ToLower() == "href" || a.Name.ToLower() == "src") &&
                            a.Value?.Contains("javascript:", StringComparison.OrdinalIgnoreCase) == true)
                            .ToList();
                        
                        foreach (var attr in urlAttrs)
                        {
                            if (attr.Name.ToLower() == "href")
                                attr.Value = "#";
                            else if (node.GetAttributeValue("data-preserve-video", "") != "true")
                                node.Remove();
                        }
                    }
                }
                
                // Nettoyer les attributs temporaires
                var preservedNodes = doc.DocumentNode.SelectNodes("//*[@data-preserve-video]");
                preservedNodes?.ToList().ForEach(n => n.Attributes["data-preserve-video"].Remove());
                
                var result = doc.DocumentNode.OuterHtml;
                
                // Compter les éléments vidéo après traitement
                var docAfter = new HtmlDocument();
                docAfter.LoadHtml(result);
                var videoElementsAfter = docAfter.DocumentNode.SelectNodes("//iframe | //video | //div[@data-video-element]")?.Count ?? 0;
                _logger.LogDebug("Éléments vidéo après traitement: {Count}", videoElementsAfter);
                
                if (videoElementsBefore != videoElementsAfter)
                {
                    _logger.LogWarning("Perte d'éléments vidéo pendant la sanitisation: {Before} -> {After}", videoElementsBefore, videoElementsAfter);
                }
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la sanitisation minimale");
                return htmlContent;
            }
        }

        // NOUVELLE MÉTHODE : Préserver explicitement les éléments vidéo
        private void PreserveVideoElements(HtmlDocument doc)
        {
            // Préserver les iframes vidéo
            var videoIframes = doc.DocumentNode.SelectNodes("//iframe[contains(@src, 'youtube.com') or contains(@src, 'youtu.be') or contains(@src, 'vimeo.com') or contains(@src, 'dailymotion.com') or contains(@src, 'player.vimeo.com')]");
            if (videoIframes != null)
            {
                foreach (var iframe in videoIframes)
                {
                    iframe.SetAttributeValue("data-preserve-video", "true");
                    _logger.LogDebug("Iframe vidéo préservé: {Src}", iframe.GetAttributeValue("src", ""));
                }
            }
            
            // Préserver les conteneurs vidéo
            var videoContainers = doc.DocumentNode.SelectNodes("//div[@data-video-element='true']");
            if (videoContainers != null)
            {
                foreach (var container in videoContainers)
                {
                    container.SetAttributeValue("data-preserve-video", "true");
                    
                    // Préserver aussi tous les enfants
                    var children = container.SelectNodes(".//*");
                    if (children != null)
                    {
                        foreach (var child in children)
                        {
                            child.SetAttributeValue("data-preserve-video", "true");
                        }
                    }
                    _logger.LogDebug("Conteneur vidéo préservé avec {Count} enfants", children?.Count ?? 0);
                }
            }
            
            // Préserver les balises vidéo directes
            var directVideos = doc.DocumentNode.SelectNodes("//video");
            if (directVideos != null)
            {
                foreach (var video in directVideos)
                {
                    video.SetAttributeValue("data-preserve-video", "true");
                    
                    // Préserver aussi les sources
                    var sources = video.SelectNodes(".//source");
                    if (sources != null)
                    {
                        foreach (var source in sources)
                        {
                            source.SetAttributeValue("data-preserve-video", "true");
                        }
                    }
                }
            }
        }

        private string StandardSanitization(string htmlContent)
        {
            if (string.IsNullOrEmpty(htmlContent))
                return string.Empty;

            try
            {
                var sanitizer = new HtmlSanitizer();

                // Configuration pour préserver la mise en forme et les vidéos
                sanitizer.AllowedTags.Clear();

                foreach (var tag in _settings.AllowedTags)
                {
                    sanitizer.AllowedTags.Add(tag);
                }

                sanitizer.AllowedAttributes.Clear();
                sanitizer.AllowDataAttributes = true;

                foreach (var kvp in _settings.AllowedAttributes)
                {
                    foreach (var attr in kvp.Value)
                    {
                        sanitizer.AllowedAttributes.Add(attr);
                    }
                }

                // CORRECTION : Ajouter plus d'attributs pour les vidéos
                var videoAttributes = new[] {
                    "data-video-element", "data-preserved-video", "data-preserve-video",
                    "contenteditable", "frameborder", "allowfullscreen", "controls",
                    "preload", "poster", "autoplay", "loop", "muted", "width", "height"
                };

                foreach (var attr in videoAttributes)
                {
                    sanitizer.AllowedAttributes.Add(attr);
                }

                sanitizer.AllowedSchemes.Clear();
                foreach (var scheme in _settings.AllowedProtocols)
                {
                    sanitizer.AllowedSchemes.Add(scheme);
                }

                // Propriétés CSS pour vidéos
                sanitizer.AllowedCssProperties.Clear();
                var cssProperties = new[] {
                    "color", "background-color", "font-size", "font-weight", "font-style",
                    "text-align", "text-decoration", "padding", "margin", "border",
                    "width", "height", "max-width", "max-height", "min-width", "min-height",
                    "display", "position", "top", "left", "right", "bottom", "float", 
                    "clear", "overflow", "z-index", "line-height", "font-family",
                    "border-radius", "box-shadow", "opacity", "visibility",
                    "padding-bottom", "aspect-ratio", "border-color", "outline-color", 
                    "text-shadow", "object-fit", "object-position"
                };

                foreach (var prop in cssProperties)
                {
                    sanitizer.AllowedCssProperties.Add(prop);
                }

                sanitizer.KeepChildNodes = true;

                // ⭐ CORRECTION : Améliorer la préservation des URLs avec gestion des URLs locales
                sanitizer.FilterUrl += (sender, e) =>
                {
                    // ⭐ CORRECTION : Autoriser les URLs locales
                    if (e.OriginalUrl.StartsWith("/uploads/"))
                    {
                        e.SanitizedUrl = e.OriginalUrl;
                        _logger.LogDebug("URL locale préservée: {Url}", e.OriginalUrl);
                        return;
                    }

                    var isVideoUrl = IsAllowedVideoUrl(e.OriginalUrl);
                    var isImageUrl = IsAllowedImageUrl(e.OriginalUrl);
                    
                    if (isVideoUrl || isImageUrl)
                    {
                        e.SanitizedUrl = e.OriginalUrl;
                        if (isVideoUrl)
                        {
                            _logger.LogDebug("URL vidéo préservée: {Url}", e.OriginalUrl);
                        }
                        else
                        {
                            _logger.LogDebug("URL image préservée: {Url}", e.OriginalUrl);
                        }
                    }
                    else
                    {
                        _logger.LogDebug("URL standard filtrée: {Url}", e.OriginalUrl);
                    }
                };

                var result = sanitizer.Sanitize(htmlContent);
                _logger.LogDebug("Sanitisation standard terminée. Longueur: {Length}", result.Length);
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la sanitisation du contenu HTML");
                return MinimalSanitization(htmlContent);
            }
        }

        private string StrictSanitization(string htmlContent)
        {
            if (string.IsNullOrEmpty(htmlContent))
                return string.Empty;

            try
            {
                var sanitizer = new HtmlSanitizer();
                
                // Configuration très stricte
                sanitizer.AllowedTags.Clear();
                sanitizer.AllowedAttributes.Clear();
                sanitizer.AllowDataAttributes = false;
                
                // Autoriser seulement les tags et attributs de base
                var basicTags = new[] { "p", "br", "strong", "em", "u", "a", "ul", "ol", "li", "img" };
                foreach (var tag in basicTags)
                {
                    sanitizer.AllowedTags.Add(tag);
                }

                // Attribuer uniquement les attributs essentiels
                sanitizer.AllowedAttributes.Add("href");
                sanitizer.AllowedAttributes.Add("src");
                sanitizer.AllowedAttributes.Add("alt");

                return sanitizer.Sanitize(htmlContent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la sanitisation stricte du contenu HTML");
                return htmlContent;
            }
        }

        private async Task<bool> ValidateCsrfTokenAsync(string token)
        {
            // Implémentation basique - à améliorer selon vos besoins
            return !string.IsNullOrEmpty(token) && token.Length > 10;
        }

        // Ajoutez cette méthode privée pour fournir les types MIME autorisés
        private string[] GetAllowedMimeTypes()
        {
            return new[]
            {
                "application/pdf",
                "application/msword",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                "text/plain",
                "application/zip",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "application/vnd.ms-excel",
                "application/vnd.openxmlformats-officedocument.presentationml.presentation"
            };
        }

        private bool IsValidFileSignature(byte[] buffer, string extension)
        {
            // Signatures de fichiers courantes (magic numbers)
            var signatures = new Dictionary<string, List<byte[]>>
            {
                { ".pdf", new List<byte[]> { new byte[] { 0x25, 0x50, 0x44, 0x46 } } }, // %PDF
                { ".doc", new List<byte[]> { new byte[] { 0xD0, 0xCF, 0x11, 0xE0 } } }, // DOC (OLE)
                { ".docx", new List<byte[]> { new byte[] { 0x50, 0x4B, 0x03, 0x04 } } }, // DOCX (ZIP)
                { ".xlsx", new List<byte[]> { new byte[] { 0x50, 0x4B, 0x03, 0x04 } } }, // XLSX (ZIP)
                { ".pptx", new List<byte[]> { new byte[] { 0x50, 0x4B, 0x03, 0x04 } } }, // PPTX (ZIP)
                { ".xls", new List<byte[]> { new byte[] { 0xD0, 0xCF, 0x11, 0xE0 } } }, // XLS (OLE)
                { ".txt", new List<byte[]> { new byte[] { 0xEF, 0xBB, 0xBF }, new byte[] { 0xFF, 0xFE }, new byte[] { 0xFE, 0xFF } } }, // UTF BOMs
                { ".zip", new List<byte[]> { new byte[] { 0x50, 0x4B, 0x03, 0x04 } } }, // ZIP
            };

            if (signatures.TryGetValue(extension, out var sigList))
            {
                foreach (var sig in sigList)
                {
                    if (buffer.Take(sig.Length).SequenceEqual(sig))
                        return true;
                }
                // Pour .txt, autoriser aussi l'absence de BOM (texte brut)
                if (extension == ".txt")
                    return true;
                return false;
            }
            // Si extension non reconnue, refuser par défaut
            return false;
        }

        private bool IsAllowedVideoUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return false;
                
            try
            {
                var uri = new Uri(url);
                var hostname = uri.Host.ToLower();
                
                // Vérifier si c'est une plateforme vidéo autorisée
                return _settings.AllowedVideoPlatforms.Any(platform => 
                    hostname.Contains(platform.ToLower()));
            }
            catch
            {
                return false;
            }
        }

        private bool IsAllowedImageUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return false;

            // ⭐ CORRECTION : Autoriser directement les URLs locales
            if (url.StartsWith("/uploads/"))
                return true;
                
            try
            {
                var uri = new Uri(url);
                
                // Vérifier si c'est une image
                var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".svg", ".bmp" };
                var path = uri.AbsolutePath.ToLower();
                
                return imageExtensions.Any(ext => path.EndsWith(ext)) || 
                       path.Contains("/uploads/images/") ||
                       uri.Host.Contains("imgur.com") ||
                       uri.Host.Contains("unsplash.com");
            }
            catch
            {
                // Si on ne peut pas créer l'URI, vérifier si c'est une URL relative
                return url.StartsWith("/uploads/");
            }
        }


        private void PreserveColorFormatting(HtmlNode node)
        {
            // Préserver les styles de couleur sur les balises de formatage
            if (node.Attributes["style"] != null)
            {
                var style = node.Attributes["style"].Value;
                // Ici, vous pouvez ajouter une logique pour filtrer/valider les propriétés de couleur si besoin
                node.Attributes["style"].Value = style; // (optionnel : nettoyer ou valider la valeur)
            }
            // Préserver l'attribut color sur <font>
            if (node.Name.Equals("font", StringComparison.OrdinalIgnoreCase) && node.Attributes["color"] != null)
            {
                var color = node.Attributes["color"].Value;
                node.Attributes["color"].Value = color; // (optionnel : valider la couleur)
            }
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

    public enum SanitizationLevel
    {
        None,      // Aucune sanitisation
        Minimal,   // Supprimer seulement <script> et événements JS
        Standard,  // Configuration actuelle
        Strict     // Très restrictif
    }
}