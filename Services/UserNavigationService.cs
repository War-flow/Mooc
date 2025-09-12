using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Mooc.Data;

namespace Mooc.Services
{
    public interface IUserNavigationService
    {
        Task<(ApplicationUser? user, string? role)> GetUserDataAsync(string userName);
    }

    public class UserNavigationService : IUserNavigationService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;

        public UserNavigationService(IDbContextFactory<ApplicationDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public async Task<(ApplicationUser? user, string? role)> GetUserDataAsync(string userName)
        {
            using var context = _contextFactory.CreateDbContext();
            
            // Utiliser directement Entity Framework au lieu de UserManager
            var user = await context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.UserName == userName);
                
            if (user == null) return (null, null);

            // Récupérer les rôles directement via EF
            var userRoles = await context.UserRoles
                .AsNoTracking()
                .Where(ur => ur.UserId == user.Id)
                .Join(context.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name)
                .ToListAsync();

            var primaryRole = GetPrimaryRole(userRoles);

            return (user, primaryRole);
        }

        private static string? GetPrimaryRole(IList<string> roles)
        {
            if (roles.Contains("Admin")) return "Admin";
            if (roles.Contains("Formateur")) return "Formateur";
            if (roles.Contains("Utilisateur")) return "Utilisateur";
            return roles.FirstOrDefault();
        }
    }
}