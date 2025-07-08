using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Mooc.Data;

namespace Mooc.Components.Account
{
    // Remove the "else if (EmailSender is IdentityNoOpEmailSender)" block from RegisterConfirmation.razor after updating with a real implementation.
    internal sealed class IdentityNoOpEmailSender : IEmailSender<ApplicationUser>
    {
        private readonly IEmailSender emailSender = new NoOpEmailSender();

        public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink) =>
            emailSender.SendEmailAsync(email, "Confirmez votre e-mail", $"Veuillez confirmer votre compte en<a href='{confirmationLink}'>en cliquant ici</a>.");

        public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink) =>
            emailSender.SendEmailAsync(email, "Réinitialisez votre mot de passe", $"Veuillez réinitialiser votre mot de passe avant <a href='{resetLink}'>en cliquant ici</a>.");

        public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode) =>
            emailSender.SendEmailAsync(email, "Réinitialisez votre mot de passe", $"Veuillez réinitialiser votre mot de passe en utilisant le code suivant : {resetCode}");
    }
}
