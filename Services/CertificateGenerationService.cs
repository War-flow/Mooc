using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DocumentFormat.OpenXml;
using iText.Html2pdf;
using iText.Html2pdf.Resolver.Font;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Mooc.Data;
using Word = DocumentFormat.OpenXml.Wordprocessing;

namespace Mooc.Services
{
    public interface ICertificateGenerationService
    {
        Task<byte[]> GenerateCertificateAsync(int sessionId, string userId, CertificateType type = CertificateType.Pdf);
        Task<byte[]> GenerateWordCertificateAsync(int sessionId, string userId);
        Task<string> GenerateHtmlCertificateAsync(int sessionId, string userId);
    }

    public enum CertificateType
    {
        Pdf,
        Word,
        Html
    }

    public class CertificateData
    {
        public string FullName { get; init; } = string.Empty;
        public string SessionTitle { get; init; } = string.Empty;
        public string DeliveryDate { get; init; } = string.Empty;
        public string CertificateNumber { get; init; } = string.Empty;
        public int CompletedCourses { get; init; }
        public int TotalRequiredCourses { get; init; }
        public DateTime SessionStartDate { get; init; }
        public DateTime SessionEndDate { get; init; }
    }

    public class CertificateGenerationOptions
    {
        public string CertificateNumberPrefix { get; set; } = "CERT";
        public string DateFormat { get; set; } = "dd MMMM yyyy";
        public string CultureInfo { get; set; } = "fr-FR";
        public string OrganizationName { get; set; } = "POINT COM";
        public string SignatoryName { get; set; } = "Christine ILLIDO";
        public string SignatoryTitle { get; set; } = "Directrice du centre";
        public string HtmlTemplatePath { get; set; } = "templates/certificate-template.html";
    }

    public class CertificateGenerationService : ICertificateGenerationService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<CertificateGenerationService> _logger;
        private readonly CertificateGenerationOptions _options;

        public CertificateGenerationService(
            IDbContextFactory<ApplicationDbContext> contextFactory,
            IWebHostEnvironment environment,
            ILogger<CertificateGenerationService> logger,
            IOptions<CertificateGenerationOptions> options)
        {
            _contextFactory = contextFactory;
            _environment = environment;
            _logger = logger;
            _options = options.Value;
        }

        public async Task<byte[]> GenerateCertificateAsync(int sessionId, string userId, CertificateType type = CertificateType.Pdf)
        {
            // Sauvegarder le certificat en base de données
            await SaveCertificateToDbAsync(sessionId, userId, type);
            
            switch (type)
            {
                case CertificateType.Word:
                    return await GenerateWordCertificateAsync(sessionId, userId);
                case CertificateType.Html:
                    var html = await GenerateHtmlCertificateAsync(sessionId, userId);
                    return System.Text.Encoding.UTF8.GetBytes(html);
                case CertificateType.Pdf:
                default:
                    return await GeneratePdfCertificateAsync(sessionId, userId);
            }
        }

        public async Task<string> GenerateHtmlCertificateAsync(int sessionId, string userId)
        {
            ValidateParameters(sessionId, userId);

            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                var certificateData = await GetCertificateDataAsync(context, sessionId, userId);
                
                return await CreateHtmlCertificateFromTemplate(certificateData);
            }
            catch (Exception ex) when (!(ex is ArgumentException || ex is InvalidOperationException || ex is UnauthorizedAccessException))
            {
                _logger.LogError(ex, "Erreur inattendue lors de la génération du certificat HTML pour la session {SessionId} et l'utilisateur {UserId}", sessionId, userId);
                throw new InvalidOperationException("Erreur lors de la génération du certificat", ex);
            }
        }

        private async Task<byte[]> GeneratePdfCertificateAsync(int sessionId, string userId)
        {
            try
            {
                var htmlContent = await GenerateHtmlCertificateAsync(sessionId, userId);
                
                using var pdfStream = new MemoryStream();
                var converterProperties = new ConverterProperties();
                
                // Configuration pour améliorer la qualité du PDF
                converterProperties.SetFontProvider(new DefaultFontProvider(true, true, true));
                
                HtmlConverter.ConvertToPdf(htmlContent, pdfStream, converterProperties);
                
                return pdfStream.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la conversion HTML vers PDF");
                throw new InvalidOperationException("Impossible de convertir le document en PDF", ex);
            }
        }

        private async Task<string> CreateHtmlCertificateFromTemplate(CertificateData data)
        {
            try
            {
                var templatePath = Path.Combine(_environment.WebRootPath, _options.HtmlTemplatePath);
                
                if (!File.Exists(templatePath))
                {
                    _logger.LogWarning("Template HTML introuvable : {TemplatePath}. Utilisation du template par défaut.", templatePath);
                    return GenerateFallbackHtml(data);
                }

                var templateContent = await File.ReadAllTextAsync(templatePath);
                
                // Générer l'URL complète du logo ou encoder en Base64
                var logoUrl = await GetLogoDataUrl();

                // Générer l'URL complète de la médaille ou encoder en Base64
                var medalUrl = await GetMedalDataUrl();

                // Remplacer les placeholders par les vraies valeurs
                var replacements = new Dictionary<string, string>
                {
                    {"{{MainTitle}}", "CERTIFICAT"},
                    {"{{SecondTitle}}", "DE FORMATION"},
                    {"{{Subtitle}}", "ATTESTATION DE RÉUSSITE"},
                    {"{{IntroText}}", "est fièrement décerné par Point Com à"},
                    {"{{FullName}}", data.FullName},
                    {"{{CompletionText}}", "pour avoir terminé avec succès son parcours de formation"},
                    {"{{SessionTitle}}", data.SessionTitle},
                    {"{{FormationPeriod}}", $"Formation dispensée du {data.SessionStartDate:dd MMMM yyyy} au {data.SessionEndDate:dd MMMM yyyy}"},
                    {"{{DeliveryText}}", $"Délivré le {data.DeliveryDate}"},
                    {"{{OrganizationText}}", $"par {_options.OrganizationName}"},
                    {"{{CertificateNumberText}}", $"Certificat n° {data.CertificateNumber}"},
                    {"{{SignatoryName}}", _options.SignatoryName},
                    {"{{SignatoryTitle}}", _options.SignatoryTitle},
                    {"{{LogoUrl}}", logoUrl},
                    {"{{MedalUrl}}", medalUrl}
                };

                return ReplaceTemplatePlaceholders(templateContent, replacements);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la création du certificat à partir du template");
                return GenerateFallbackHtml(data);
            }
        }

        private async Task<string> GetLogoDataUrl()
        {
            try
            {
                var logoPath = Path.Combine(_environment.WebRootPath, "images", "logo.png");
                
                if (!File.Exists(logoPath))
                {
                    _logger.LogWarning("Fichier logo introuvable : {LogoPath}", logoPath);
                    return ""; // Retourner une chaîne vide si le logo n'existe pas
                }

                // Lire le fichier logo.png et l'encoder en Base64
                var logoBytes = await File.ReadAllBytesAsync(logoPath);
                var mimeType = Path.GetExtension(logoPath).ToLower() switch
                {
                    ".png" => "image/png",
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".gif" => "image/gif",
                    ".svg" => "image/svg+xml",
                    _ => "image/png"
                };

                var base64String = Convert.ToBase64String(logoBytes);
                return $"data:{mimeType};base64,{base64String}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la lecture du fichier logo");
                return "";
            }
        }

        private async Task<string> GetMedalDataUrl()
        {
            try
            {
                var medalPath = Path.Combine(_environment.WebRootPath, "images", "medal.png");

                if (!File.Exists(medalPath))
                {
                    _logger.LogWarning("Fichier médaille introuvable : {MedalPath}", medalPath);
                    return ""; // Retourner une chaîne vide si la médaille n'existe pas
                }

                // Lire le fichier medal.png et l'encoder en Base64
                var medalBytes = await File.ReadAllBytesAsync(medalPath);
                var mimeType = Path.GetExtension(medalPath).ToLower() switch
                {
                    ".png" => "image/png",
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".gif" => "image/gif",
                    ".svg" => "image/svg+xml",
                    _ => "image/png"
                };

                var base64String = Convert.ToBase64String(medalBytes);
                return $"data:{mimeType};base64,{base64String}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la lecture du fichier médaille");
                return "";
            }
        }

        private string ReplaceTemplatePlaceholders(string template, Dictionary<string, string> replacements)
        {
            var result = template;
            
            foreach (var replacement in replacements)
            {
                result = result.Replace(replacement.Key, replacement.Value, StringComparison.OrdinalIgnoreCase);
            }
            
            return result;
        }

        public async Task<byte[]> GenerateWordCertificateAsync(int sessionId, string userId)
        {
            ValidateParameters(sessionId, userId);

            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                var certificateData = await GetCertificateDataAsync(context, sessionId, userId);
                
                return CreateWordCertificate(certificateData);
            }
            catch (Exception ex) when (!(ex is ArgumentException || ex is InvalidOperationException || ex is UnauthorizedAccessException))
            {
                _logger.LogError(ex, "Erreur inattendue lors de la génération du certificat Word pour la session {SessionId} et l'utilisateur {UserId}", sessionId, userId);
                throw new InvalidOperationException("Erreur lors de la génération du certificat", ex);
            }
        }

        private static void ValidateParameters(int sessionId, string userId)
        {
            if (sessionId <= 0)
                throw new ArgumentException("L'ID de session doit être positif", nameof(sessionId));
            
            if (string.IsNullOrWhiteSpace(userId))
                throw new ArgumentException("L'ID utilisateur ne peut pas être vide", nameof(userId));
        }

        private async Task<CertificateData> GetCertificateDataAsync(ApplicationDbContext context, int sessionId, string userId)
        {
            var session = await context.Sessions
                .Include(s => s.Courses)
                .FirstOrDefaultAsync(s => s.Id == sessionId);
            
            var user = await context.Users
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (session == null)
                throw new InvalidOperationException($"Aucune session trouvée avec l'ID {sessionId}");
            
            if (user == null)
                throw new InvalidOperationException($"Aucun utilisateur trouvé avec l'ID {userId}");

            // Vérifier l'inscription
            var isEnrolled = await context.Sessions
                .Where(s => s.Id == sessionId)
                .SelectMany(s => s.EnrolledUsers)
                .AnyAsync(u => u.Id == userId);
            
            if (!isEnrolled)
                throw new UnauthorizedAccessException("L'utilisateur n'est pas inscrit à cette session");

            var completedCourses = await GetCompletedCoursesCountAsync(context, sessionId, userId);
            var totalRequiredCourses = await GetTotalRequiredCoursesCountAsync(context, sessionId);

            // Récupérer le certificat existant depuis la base de données
            var existingCertificate = await context.Certificates
                .FirstOrDefaultAsync(c => c.UserId == userId && c.SessionId == sessionId);

            // Utiliser le numéro de certificat existant ou en générer un nouveau
            string certificateNumber;
            if (existingCertificate != null && !string.IsNullOrEmpty(existingCertificate.CertificateNumber))
            {
                certificateNumber = existingCertificate.CertificateNumber;
            }
            else
            {
                certificateNumber = GenerateCertificateNumber();
            }

            return new CertificateData
            {
                FullName = $"{user.FirstName} {user.LastName}",
                SessionTitle = session.Title,
                DeliveryDate = DateTime.Now.ToString(_options.DateFormat, new System.Globalization.CultureInfo(_options.CultureInfo)),
                CertificateNumber = certificateNumber,
                CompletedCourses = completedCourses,
                TotalRequiredCourses = totalRequiredCourses,
                SessionStartDate = session.StartDate,
                SessionEndDate = session.EndDate
            };
        }

        // Conserver les méthodes existantes pour la génération Word...
        private byte[] CreateWordCertificate(CertificateData data)
        {
            using var memoryStream = new MemoryStream();
            using var wordDoc = WordprocessingDocument.Create(memoryStream, WordprocessingDocumentType.Document);
            
            // Créer les parties principales du document
            var mainPart = wordDoc.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());

            // Configuration de la page (A4, orientation paysage)
            var sectionProperties = new SectionProperties();
            var pageSize = new PageSize() 
            { 
                Width = 16838U,  // A4 paysage largeur
                Height = 11906U, // A4 paysage hauteur
                Orient = PageOrientationValues.Landscape 
            };
            var pageMargin = new PageMargin() 
            { 
                Top = 720, 
                Right = 720U, 
                Bottom = 720, 
                Left = 720U 
            };
            sectionProperties.Append(pageSize, pageMargin);

            // Titre principal du certificat
            body.AppendChild(CreateTitle("CERTIFICAT DE FORMATION", 28, true));
            body.AppendChild(CreateEmptyParagraph());

            // Sous-titre
            body.AppendChild(CreateCenteredParagraph("ATTESTATION DE RÉUSSITE", 18, false));
            body.AppendChild(CreateEmptyParagraph());
            body.AppendChild(CreateEmptyParagraph());

            // Corps du certificat
            body.AppendChild(CreateCenteredParagraph("Il est certifié que", 14, false));
            body.AppendChild(CreateEmptyParagraph());

            // Nom de la personne (en gras et plus grand)
            body.AppendChild(CreateTitle(data.FullName, 20, true));
            body.AppendChild(CreateEmptyParagraph());

            // Texte principal
            body.AppendChild(CreateCenteredParagraph("a suivi avec succès la formation", 14, false));
            body.AppendChild(CreateEmptyParagraph());

            // Titre de la formation (en gras)
            body.AppendChild(CreateTitle($"« {data.SessionTitle} »", 16, true));
            body.AppendChild(CreateEmptyParagraph());

            // Informations sur la formation
            var formationInfo = $"Formation dispensée du {data.SessionStartDate:dd MMMM yyyy} au {data.SessionEndDate:dd MMMM yyyy}";
            body.AppendChild(CreateCenteredParagraph(formationInfo, 12, false));
            
            var courseInfo = $"Cours complétés : {data.CompletedCourses} sur {data.TotalRequiredCourses}";
            body.AppendChild(CreateCenteredParagraph(courseInfo, 12, false));
            body.AppendChild(CreateEmptyParagraph());
            body.AppendChild(CreateEmptyParagraph());

            // Date et lieu de délivrance
            body.AppendChild(CreateCenteredParagraph($"Délivré le {data.DeliveryDate}", 12, false));
            body.AppendChild(CreateCenteredParagraph($"par {_options.OrganizationName}", 12, false));
            body.AppendChild(CreateEmptyParagraph());

            // Numéro du certificat
            body.AppendChild(CreateCenteredParagraph($"Certificat n° {data.CertificateNumber}", 10, false));
            body.AppendChild(CreateEmptyParagraph());
            body.AppendChild(CreateEmptyParagraph());

            // Signature
            var signatureTable = CreateSignatureTable();
            body.AppendChild(signatureTable);

            // Ajouter les propriétés de section à la fin
            body.AppendChild(sectionProperties);

            wordDoc.Save();
            return memoryStream.ToArray();
        }

        private Word.Paragraph CreateTitle(string text, int fontSize, bool bold)
        {
            var paragraph = new Word.Paragraph();
            var paragraphProperties = new Word.ParagraphProperties();
            paragraphProperties.Append(new Justification() { Val = JustificationValues.Center });
            paragraph.Append(paragraphProperties);

            var run = new Word.Run();
            var runProperties = new Word.RunProperties();
            runProperties.Append(new Word.FontSize() { Val = (fontSize * 2).ToString() }); // Points * 2 pour OpenXML
            runProperties.Append(new Word.FontSizeComplexScript() { Val = (fontSize * 2).ToString() });

            if (bold)
            {
                runProperties.Append(new Bold());
                runProperties.Append(new BoldComplexScript());
            }

            run.Append(runProperties);
            run.Append(new Word.Text(text));
            paragraph.Append(run);

            return paragraph;
        }

        private Word.Paragraph CreateCenteredParagraph(string text, int fontSize, bool bold)
        {
            var paragraph = new Word.Paragraph();
            var paragraphProperties = new Word.ParagraphProperties();
            paragraphProperties.Append(new Justification() { Val = JustificationValues.Center });
            paragraph.Append(paragraphProperties);

            var run = new Word.Run();
            var runProperties = new Word.RunProperties();
            runProperties.Append(new FontSize() { Val = (fontSize * 2).ToString() });
            runProperties.Append(new Word.FontSizeComplexScript() { Val = (fontSize * 2).ToString() });

            if (bold)
            {
                runProperties.Append(new Bold());
                runProperties.Append(new BoldComplexScript());
            }

            run.Append(runProperties);
            run.Append(new Word.Text(text));
            paragraph.Append(run);

            return paragraph;
        }

        private Word.Paragraph CreateEmptyParagraph()
        {
            var paragraph = new Word.Paragraph();
            var run = new Word.Run();
            var runProperties = new Word.RunProperties();
            runProperties.Append(new FontSize() { Val = "24" }); // 12pt
            run.Append(runProperties);
            run.Append(new Word.Text(" "));
            paragraph.Append(run);
            return paragraph;
        }

        private Word.Table CreateSignatureTable()
        {
            var table = new Word.Table();

            // Propriétés du tableau
            var tableProperties = new Word.TableProperties();
            var tableStyle = new Word.TableStyle() { Val = "TableGrid" };
            var tableWidth = new Word.TableWidth() { Width = "100", Type = Word.TableWidthUnitValues.Pct };
            var tableLook = new Word.TableLook() { Val = "04A0" };
            tableProperties.Append(tableStyle, tableWidth, tableLook);
            table.AppendChild(tableProperties);

            // Ligne du tableau
            var tableRow = new Word.TableRow();

            // Cellule gauche (vide)
            var leftCell = new Word.TableCell();
            var leftCellProperties = new Word.TableCellProperties();
            var leftCellWidth = new Word.TableCellWidth() { Type = Word.TableWidthUnitValues.Pct, Width = "50" };
            leftCellProperties.Append(leftCellWidth);
            leftCell.Append(leftCellProperties);
            leftCell.Append(new Word.Paragraph());

            // Cellule droite (signature)
            var rightCell = new Word.TableCell();
            var rightCellProperties = new Word.TableCellProperties();
            var rightCellWidth = new Word.TableCellWidth() { Type = Word.TableWidthUnitValues.Pct, Width = "50" };
            rightCellProperties.Append(rightCellWidth);
            rightCell.Append(rightCellProperties);

            // Contenu de la signature
            rightCell.Append(CreateCenteredParagraph(_options.SignatoryName, 12, true));
            rightCell.Append(CreateCenteredParagraph(_options.SignatoryTitle, 10, false));

            tableRow.Append(leftCell, rightCell);
            table.Append(tableRow);

            return table;
        }

        private async Task<int> GetCompletedCoursesCountAsync(ApplicationDbContext context, int sessionId, string userId)
        {
            return await context.CourseProgresses
                .Where(cp => cp.UserId == userId && cp.IsCompleted)
                .Join(context.Courses, cp => cp.CoursId, c => c.Id, (cp, c) => c)
                .Where(c => c.SessionId == sessionId && c.IsRequired && c.IsPublished)
                .CountAsync();
        }

        private async Task<int> GetTotalRequiredCoursesCountAsync(ApplicationDbContext context, int sessionId)
        {
            return await context.Courses
                .CountAsync(c => c.SessionId == sessionId && c.IsRequired && c.IsPublished);
        }

        private string GenerateCertificateNumber()
        {
            var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
            var random = Random.Shared.Next(1000, 9999);
            return $"{_options.CertificateNumberPrefix}-{timestamp}-{random}";
        }

        private string GenerateFallbackHtml(CertificateData data)
        {
            return $@"
            <!DOCTYPE html>
            <html>
            <head>
                <meta charset='utf-8'>
                <style>
                    @page {{ size: A4 landscape; margin: 2cm; }}
                    body {{ font-family: 'Times New Roman', serif; margin: 0; padding: 40px; text-align: center; }}
                    .certificate {{ border: 3px solid #2c3e50; padding: 60px; background: white; }}
                    .title {{ font-size: 28px; font-weight: bold; margin-bottom: 30px; color: #2c3e50; }}
                    .content {{ font-size: 16px; line-height: 1.6; color: #555; }}
                    .name {{ font-size: 24px; font-weight: bold; color: #e74c3c; margin: 20px 0; }}
                </style>
            </head>
            <body>
                <div class='certificate'>
                    <div class='title'>CERTIFICAT DE FORMATION</div>
                    <div class='content'>
                        <p>Il est certifié que</p>
                        <div class='name'>{data.FullName}</div>
                        <p>a suivi avec succès la formation</p>
                        <p><strong>{data.SessionTitle}</strong></p>
                        <p>Délivré le {data.DeliveryDate}</p>
                        <p>par {_options.OrganizationName}</p>
                        <p><small>Certificat n° {data.CertificateNumber}</small></p>
                    </div>
                </div>
            </body>
            </html>";
        }

        private async Task SaveCertificateToDbAsync(int sessionId, string userId, CertificateType type)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                
                var existingCertificate = await context.Certificates
                    .FirstOrDefaultAsync(c => c.UserId == userId && c.SessionId == sessionId);

                if (existingCertificate == null)
                {
                    var session = await context.Sessions.FindAsync(sessionId);
                    
                    var certificate = new Certificate
                    {
                        UserId = userId,
                        SessionId = sessionId,
                        Title = $"Certificat - {session?.Title ?? "Formation"}",
                        DateGenerated = DateTime.Now,
                        DateDelivered = DateTime.Now,
                        Status = "Generated",
                        CertificateNumber = GenerateCertificateNumber()
                    };

                    context.Certificates.Add(certificate);
                    await context.SaveChangesAsync();
                    
                    _logger.LogInformation("Certificat sauvegardé pour l'utilisateur {UserId} et la session {SessionId}", userId, sessionId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la sauvegarde du certificat en base pour l'utilisateur {UserId} et la session {SessionId}", userId, sessionId);
            }
        }
    }
}