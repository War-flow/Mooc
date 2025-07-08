using Microsoft.AspNetCore.Identity;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Mooc.Services
{
    public class RoleClaimService
    {
        private readonly RoleManager<IdentityRole> _roleManager;

        public RoleClaimService(RoleManager<IdentityRole> roleManager)
        {
            _roleManager = roleManager;
        }

        /// <summary>
        /// Ajoute une revendication à un rôle
        /// </summary>
        public async Task<bool> AddClaimToRoleAsync(string roleName, string claimType, string claimValue)
        {
            var role = await _roleManager.FindByNameAsync(roleName);
            if (role == null)
                return false;

            // Vérifier si le claim existe déjà
            var claims = await _roleManager.GetClaimsAsync(role);
            if (claims.Any(c => c.Type == claimType && c.Value == claimValue))
                return true;  // Le claim existe déjà

            // Ajouter le nouveau claim
            var result = await _roleManager.AddClaimAsync(role, new Claim(claimType, claimValue));
            return result.Succeeded;
        }

        /// <summary>
        /// Supprime une revendication d'un rôle
        /// </summary>
        public async Task<bool> RemoveClaimFromRoleAsync(string roleName, string claimType, string claimValue)
        {
            var role = await _roleManager.FindByNameAsync(roleName);
            if (role == null)
                return false;

            var claims = await _roleManager.GetClaimsAsync(role);
            var claim = claims.FirstOrDefault(c => c.Type == claimType && c.Value == claimValue);

            if (claim == null)
                return true;  // Le claim n'existe pas, donc pas besoin de le supprimer

            var result = await _roleManager.RemoveClaimAsync(role, claim);
            return result.Succeeded;
        }

        /// <summary>
        /// Récupère toutes les revendications d'un rôle
        /// </summary>
        public async Task<IList<Claim>> GetRoleClaimsAsync(string roleName)
        {
            var role = await _roleManager.FindByNameAsync(roleName);
            if (role == null)
                return new List<Claim>();

            return await _roleManager.GetClaimsAsync(role);
        }
    }
}