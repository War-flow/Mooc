using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Mooc.Data;
using System.Net;
using System.Net.Mail;
using System.Text;

namespace Mooc.Services
{
    public class EmailSender : IEmailSender<ApplicationUser>, IDisposable
    {
        private readonly EmailSettings _emailSettings;
        private readonly ILogger<EmailSender> _logger;
        private bool _disposed = false;

        public EmailSender(IOptions<EmailSettings> emailSettings, ILogger<EmailSender> logger)
        {
            _emailSettings = emailSettings.Value;
            _logger = logger;
            
            // Log de diagnostic pour vérifier la configuration
            _logger.LogInformation("Configuration EmailSender - Server: {Server}, Port: {Port}, EnableSsl: {EnableSsl}, FromEmail: {FromEmail}", 
                _emailSettings.SmtpServer, _emailSettings.Port, _emailSettings.EnableSsl, _emailSettings.FromEmail);
            
            if (string.IsNullOrEmpty(_emailSettings.SmtpServer))
            {
                _logger.LogError("Configuration SMTP manquante : SmtpServer est vide");
                throw new InvalidOperationException("Configuration SMTP manquante : SmtpServer est vide");
            }
        }

        public async Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink)
        {
            _logger.LogInformation("Tentative d'envoi d'email de confirmation à {Email}", email);
            
            var subject = "Confirmez votre adresse e-mail";
            var htmlMessage = BuildEmailTemplate(
                "Confirmation de votre compte",
                $"Bonjour {user.FirstName ?? user.UserName},",
                "Merci de vous être inscrit. Veuillez confirmer votre adresse e-mail en cliquant sur le lien ci-dessous :",
                confirmationLink,
                "Confirmer mon e-mail",
                "#007bff",
                "Si vous n'avez pas créé ce compte, vous pouvez ignorer cet e-mail."
            );

            await SendEmailWithRetryAsync(email, subject, htmlMessage);
        }

        public async Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink)
        {
            _logger.LogInformation("Tentative d'envoi d'email de réinitialisation à {Email}", email);
            
            var subject = "Réinitialisation de votre mot de passe";
            var htmlMessage = BuildEmailTemplate(
                "Réinitialisation de mot de passe",
                $"Bonjour {user.FirstName ?? user.UserName},",
                "Une demande de réinitialisation de mot de passe a été effectuée pour votre compte. Cliquez sur le lien ci-dessous pour réinitialiser votre mot de passe :",
                resetLink,
                "Réinitialiser mon mot de passe",
                "#dc3545",
                "Ce lien expirera dans 24 heures pour des raisons de sécurité. Si vous n'avez pas demandé cette réinitialisation, vous pouvez ignorer cet e-mail."
            );

            await SendEmailWithRetryAsync(email, subject, htmlMessage);
        }

        public async Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode)
        {
            _logger.LogInformation("Tentative d'envoi de code de réinitialisation à {Email}", email);
            
            var subject = "Code de réinitialisation de mot de passe";
            var htmlMessage = BuildCodeEmailTemplate(
                "Code de réinitialisation",
                $"Bonjour {user.FirstName ?? user.UserName},",
                "Voici votre code de réinitialisation de mot de passe :",
                resetCode,
                "Ce code expire dans 15 minutes."
            );

            await SendEmailWithRetryAsync(email, subject, htmlMessage);
        }

        private async Task SendEmailWithRetryAsync(string email, string subject, string htmlMessage)
        {
            var attempt = 0;
            Exception? lastException = null;

            _logger.LogInformation("Début d'envoi d'email à {Email} avec le sujet '{Subject}'", email, subject);

            while (attempt < _emailSettings.MaxRetries)
            {
                SmtpClient? smtpClient = null;
                try
                {
                    // Créer un nouveau client SMTP pour chaque tentative
                    smtpClient = new SmtpClient(_emailSettings.SmtpServer)
                    {
                        Port = _emailSettings.Port,
                        Credentials = new NetworkCredential(_emailSettings.Username, _emailSettings.Password),
                        EnableSsl = _emailSettings.EnableSsl,
                        Timeout = _emailSettings.TimeoutMs,
                        DeliveryMethod = SmtpDeliveryMethod.Network,
                        UseDefaultCredentials = false
                    };

                    using var mailMessage = new MailMessage
                    {
                        From = new MailAddress(_emailSettings.FromEmail, _emailSettings.FromName),
                        Subject = subject,
                        Body = htmlMessage,
                        IsBodyHtml = true,
                        BodyEncoding = Encoding.UTF8,
                        SubjectEncoding = Encoding.UTF8
                    };

                    mailMessage.To.Add(email);

                    _logger.LogInformation("Envoi de l'email via SMTP {Server}:{Port} (tentative {Attempt}) - SSL: {EnableSsl}", 
                        _emailSettings.SmtpServer, _emailSettings.Port, attempt + 1, _emailSettings.EnableSsl);

                    await smtpClient.SendMailAsync(mailMessage);
                    _logger.LogInformation("Email envoyé avec succès à {Email} (tentative {Attempt})", 
                        email, attempt + 1);
                    return;
                }
                catch (SmtpException smtpEx)
                {
                    lastException = smtpEx;
                    attempt++;
                    
                    _logger.LogWarning(smtpEx, "Erreur SMTP lors de l'envoi à {Email} (tentative {Attempt}/{MaxRetries}) - Code: {StatusCode}", 
                        email, attempt, _emailSettings.MaxRetries, smtpEx.StatusCode);

                    if (attempt < _emailSettings.MaxRetries)
                    {
                        await Task.Delay(_emailSettings.RetryDelayMs * attempt); // Backoff exponentiel
                    }
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    attempt++;
                    
                    _logger.LogWarning(ex, "Erreur générale lors de l'envoi d'email à {Email} (tentative {Attempt}/{MaxRetries})", 
                        email, attempt, _emailSettings.MaxRetries);

                    if (attempt < _emailSettings.MaxRetries)
                    {
                        await Task.Delay(_emailSettings.RetryDelayMs * attempt); // Backoff exponentiel
                    }
                }
                finally
                {
                    smtpClient?.Dispose();
                }
            }

            _logger.LogError(lastException, "Impossible d'envoyer l'email à {Email} après {MaxRetries} tentatives", 
                email, _emailSettings.MaxRetries);
            throw new InvalidOperationException($"Échec de l'envoi d'email après {_emailSettings.MaxRetries} tentatives", lastException);
        }

        private string BuildEmailTemplate(string title, string greeting, string message, string link, string buttonText, string buttonColor, string footer)
        {
            return $@"
                <!DOCTYPE html>
                <html>
                <head>
                    <meta charset='utf-8'>
                    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
                    <title>{title}</title>
                </head>
                <body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;'>
                    <div style='background-color: #f8f9fa; padding: 30px; border-radius: 10px;'>
                        <h2 style='color: #2c3e50; margin-bottom: 20px;'>{title}</h2>
                        <p style='margin-bottom: 15px;'>{greeting}</p>
                        <p style='margin-bottom: 25px;'>{message}</p>
                        <div style='text-align: center; margin: 30px 0;'>
                            <a href='{link}' style='background-color: {buttonColor}; color: white; padding: 15px 30px; text-decoration: none; border-radius: 5px; display: inline-block; font-weight: bold;'>{buttonText}</a>
                        </div>
                        <p style='margin-top: 25px; font-size: 14px; color: #666;'>{footer}</p>
                        <hr style='border: none; border-top: 1px solid #eee; margin: 20px 0;'>
                        <p style='font-size: 12px; color: #999; text-align: center;'>Cordialement,<br>L'équipe MOOC Platform</p>
                    </div>
                </body>
                </html>";
        }

        private string BuildCodeEmailTemplate(string title, string greeting, string message, string code, string footer)
        {
            return $@"
                <!DOCTYPE html>
                <html>
                <head>
                    <meta charset='utf-8'>
                    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
                    <title>{title}</title>
                </head>
                <body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;'>
                    <div style='background-color: #f8f9fa; padding: 30px; border-radius: 10px;'>
                        <h2 style='color: #2c3e50; margin-bottom: 20px;'>{title}</h2>
                        <p style='margin-bottom: 15px;'>{greeting}</p>
                        <p style='margin-bottom: 25px;'>{message}</p>
                        <div style='text-align: center; margin: 30px 0;'>
                            <div style='font-size: 32px; font-weight: bold; background-color: #e9ecef; padding: 20px; border-radius: 8px; letter-spacing: 3px; border: 2px dashed #6c757d;'>{code}</div>
                        </div>
                        <p style='margin-top: 25px; font-size: 14px; color: #666;'>{footer}</p>
                        <hr style='border: none; border-top: 1px solid #eee; margin: 20px 0;'>
                        <p style='font-size: 12px; color: #999; text-align: center;'>Cordialement,<br>L'équipe MOOC Platform</p>
                    </div>
                </body>
                </html>";
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _disposed = true;
            }
        }
    }
}