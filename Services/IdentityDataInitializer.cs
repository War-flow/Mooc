using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Mooc.Data;
using System.Security.Claims;

namespace Mooc.Services
{
    public class IdentityDataInitializer
    {
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleClaimService _roleClaimService;
        private readonly IConfiguration _configuration;

        public IdentityDataInitializer(
            RoleManager<IdentityRole> roleManager,
            UserManager<ApplicationUser> userManager,
            RoleClaimService roleClaimService,
            IConfiguration configuration)
        {
            _roleManager = roleManager;
            _userManager = userManager;
            _roleClaimService = roleClaimService;
            _configuration = configuration;
        }

        public async Task InitializeAsync()
        {
            // Créer les rôles de base s'ils n'existent pas
            string[] roleNames = { "Admin", "Formateur", "Utilisateur" };
            foreach (var roleName in roleNames)
            {
                if (!await _roleManager.RoleExistsAsync(roleName))
                {
                    await _roleManager.CreateAsync(new IdentityRole(roleName));
                }
            }

            // Ajouter des claims au rôle Admin
            await _roleClaimService.AddClaimToRoleAsync("Admin", "Permission", "GérerUtilisateurs");
            await _roleClaimService.AddClaimToRoleAsync("Admin", "Permission", "GestionFormation");
            await _roleClaimService.AddClaimToRoleAsync("Admin", "Permission", "VoirCours");
            await _roleClaimService.AddClaimToRoleAsync("Admin", "Permission", "InscritSession");

            // Ajouter des claims au rôle Formateur
            await _roleClaimService.AddClaimToRoleAsync("Formateur", "Permission", "GestionFormation");
            await _roleClaimService.AddClaimToRoleAsync("Formateur", "Permission", "VoirCours");
            await _roleClaimService.AddClaimToRoleAsync("Formateur", "Permission", "RépondreQuiz");
            // Ajouter des claims au rôle Utilisateur
            await _roleClaimService.AddClaimToRoleAsync("Utilisateur", "Permission", "VoirCours");
            await _roleClaimService.AddClaimToRoleAsync("Utilisateur", "Permission", "RépondreQuiz");
            await _roleClaimService.AddClaimToRoleAsync("Utilisateur", "Permission", "InscritSession");
            await _roleClaimService.AddClaimToRoleAsync("Utilisateur", "Permission", "GérerCompte");

            // Création du compte administrateur si celui-ci n'existe pas
            var adminEmail = _configuration["AdminUser:Email"] ?? string.Empty;
            var adminPassword = _configuration["AdminUser:Password"] ?? string.Empty;
            var firstName = _configuration["AdminUser:FirstName"] ?? string.Empty;
            var lastName = _configuration["AdminUser:LastName"] ?? string.Empty;

            if (!string.IsNullOrEmpty(adminEmail) && !string.IsNullOrEmpty(adminPassword))
            {
                try
                {
                    var adminUser = await _userManager.FindByEmailAsync(adminEmail);

                    if (adminUser == null)
                    {
                        adminUser = new ApplicationUser
                        {
                            UserName = adminEmail,
                            Email = adminEmail,
                            EmailConfirmed = true,
                            FirstName = firstName,
                            LastName = lastName
                        };

                        // Créer l'utilisateur avec un mot de passe fort
                        var result = await _userManager.CreateAsync(adminUser, adminPassword);

                        if (result.Succeeded)
                        {
                            // Attribuer le rôle Admin à l'utilisateur
                            await _userManager.AddToRoleAsync(adminUser, "Admin");
                        }
                        else
                        {
                            // Journaliser les erreurs
                            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                            Console.WriteLine($"Échec de la création de l'utilisateur Admin: {errors}");

                            // Si le problème vient du mot de passe, essayez de créer un mot de passe plus fort
                            if (result.Errors.Any(e => e.Code.Contains("Password")))
                            {
                                // Mot de passe fort temporaire qui répond aux exigences
                                var tempPassword = "Admin@123456Abcdef!";
                                result = await _userManager.CreateAsync(adminUser, tempPassword);

                                if (result.Succeeded)
                                {
                                    await _userManager.AddToRoleAsync(adminUser, "Admin");
                                    Console.WriteLine("Administrateur créé avec un mot de passe temporaire. Veuillez le changer.");
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Capturer et journaliser toute exception
                    Console.WriteLine($"Exception lors de la création de l'administrateur: {ex.Message}");
                }
            }
        }
    }
}