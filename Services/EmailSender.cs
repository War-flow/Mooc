using Microsoft.AspNetCore.Identity;
using Mooc.Data;
using System.Net;
using System.Net.Mail;

namespace Mooc.Services
{
    public class EmailSender : IEmailSender<ApplicationUser>
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<EmailSender> _logger;

        public EmailSender(IConfiguration configuration, ILogger<EmailSender> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink)
        {
            var subject = "Confirmez votre adresse e-mail";
            var htmlMessage = $@"
                <h2>Confirmation de votre compte</h2>
                <p>Bonjour {user.UserName},</p>
                <p>Merci de vous être inscrit. Veuillez confirmer votre adresse e-mail en cliquant sur le lien ci-dessous :</p>
                <p><a href='{confirmationLink}' style='background-color: #007bff; color: white; padding: 10px 20px; text-decoration: none; border-radius: 5px;'>Confirmer mon e-mail</a></p>
                <p>Si vous n'avez pas créé ce compte, vous pouvez ignorer cet e-mail.</p>
                <p>Cordialement,<br>L'équipe MOOC</p>";

            await SendEmailAsync(email, subject, htmlMessage);
        }

        public async Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink)
        {
            var subject = "Réinitialisation de votre mot de passe";
            var htmlMessage = $@"
                <h2>Réinitialisation de mot de passe</h2>
                <p>Bonjour {user.UserName},</p>
                <p>Une demande de réinitialisation de mot de passe a été effectuée pour votre compte.</p>
                <p>Cliquez sur le lien ci-dessous pour réinitialiser votre mot de passe :</p>
                <p><a href='{resetLink}' style='background-color: #dc3545; color: white; padding: 10px 20px; text-decoration: none; border-radius: 5px;'>Réinitialiser mon mot de passe</a></p>
                <p>Ce lien expirera dans 24 heures pour des raisons de sécurité.</p>
                <p>Si vous n'avez pas demandé cette réinitialisation, vous pouvez ignorer cet e-mail.</p>
                <p>Cordialement,<br>L'équipe MOOC</p>";

            await SendEmailAsync(email, subject, htmlMessage);
        }

        public async Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode)
        {
            var subject = "Code de réinitialisation de mot de passe";
            var htmlMessage = $@"
                <h2>Code de réinitialisation</h2>
                <p>Bonjour {user.UserName},</p>
                <p>Voici votre code de réinitialisation de mot de passe :</p>
                <p style='font-size: 24px; font-weight: bold; background-color: #f8f9fa; padding: 15px; text-align: center; border-radius: 5px;'>{resetCode}</p>
                <p>Ce code expire dans 15 minutes.</p>
                <p>Cordialement,<br>L'équipe MOOC</p>";

            await SendEmailAsync(email, subject, htmlMessage);
        }

        private async Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            try
            {
                var smtpClient = new SmtpClient(_configuration["EmailSettings:SmtpServer"])
                {
                    Port = int.Parse(_configuration["EmailSettings:Port"]),
                    Credentials = new NetworkCredential(
                        _configuration["EmailSettings:Username"],
                        _configuration["EmailSettings:Password"]),
                    EnableSsl = bool.Parse(_configuration["EmailSettings:EnableSsl"])
                };

                var mailMessage = new MailMessage
                {
                    From = new MailAddress(
                        _configuration["EmailSettings:FromEmail"],
                        _configuration["EmailSettings:FromName"]),
                    Subject = subject,
                    Body = htmlMessage,
                    IsBodyHtml = true
                };

                mailMessage.To.Add(email);

                await smtpClient.SendMailAsync(mailMessage);
                _logger.LogInformation("Email envoyé avec succès à {Email}", email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'envoi de l'email à {Email}", email);
                throw;
            }
        }
    }
}