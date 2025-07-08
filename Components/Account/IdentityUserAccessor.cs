using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Mooc.Data;
using System.Collections.Concurrent;

namespace Mooc.Components.Account
{
    internal sealed class IdentityUserAccessor
    {
        private readonly UserManager<ApplicationUser> userManager;
        private readonly IdentityRedirectManager redirectManager;
        private readonly IDbContextFactory<ApplicationDbContext> dbContextFactory;
        private readonly ConcurrentDictionary<string, ApplicationUser> userCache = new();

        public IdentityUserAccessor(
            UserManager<ApplicationUser> userManager,
            IdentityRedirectManager redirectManager,
            IDbContextFactory<ApplicationDbContext> dbContextFactory)
        {
            this.userManager = userManager;
            this.redirectManager = redirectManager;
            this.dbContextFactory = dbContextFactory;
        }

        public async Task<ApplicationUser> GetRequiredUserAsync(HttpContext context)
        {
            // Obtenir l'ID utilisateur avant de faire la requête
            var userId = userManager.GetUserId(context.User);

            if (string.IsNullOrEmpty(userId))
            {
                redirectManager.RedirectToWithStatus("Account/InvalidUser",
                    "Erreur : Impossible de trouver l'ID de l'utilisateur.", context);
                return null!; // Ce code ne sera jamais atteint à cause du RedirectToWithStatus
            }

            // Vérifier si l'utilisateur est déjà en cache
            if (userCache.TryGetValue(userId, out var cachedUser))
            {
                return cachedUser;
            }

            // Créer une nouvelle instance de DbContext pour cette opération
            using var dbContext = await dbContextFactory.CreateDbContextAsync();

            // Important: Utiliser le DbContext local au lieu de userManager.Users
            var user = await dbContext.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user is null)
            {
                redirectManager.RedirectToWithStatus("Account/InvalidUser",
                    $"Erreur : Impossible de charger l'utilisateur avec l'ID'{userId}'.", context);
                return null!; // Ce code ne sera jamais atteint
            }

            // Mettre en cache l'utilisateur
            userCache.TryAdd(userId, user);

            return user;
        }
    }
}

