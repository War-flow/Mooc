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
        private readonly UserManager<ApplicationUser> _userManager;

        public UserNavigationService(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
        }

        public async Task<(ApplicationUser? user, string? role)> GetUserDataAsync(string userName)
        {
            var user = await _userManager.FindByNameAsync(userName);
            if (user == null) return (null, null);

            var roles = await _userManager.GetRolesAsync(user);
            var primaryRole = GetPrimaryRole(roles);

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