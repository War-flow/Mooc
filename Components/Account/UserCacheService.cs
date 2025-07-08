using Mooc.Data;

using System.Collections.Concurrent;

namespace Mooc.Components.Account
{
    public interface IUserCacheService
    {
        bool TryGetUser(string userId, out ApplicationUser? user);
        void AddUser(string userId, ApplicationUser user);
        void ClearCache();
    }

    public class UserCacheService : IUserCacheService
    {
        private readonly ConcurrentDictionary<string, ApplicationUser> _userCache = new();

        public bool TryGetUser(string userId, out ApplicationUser? user)
        {
            return _userCache.TryGetValue(userId, out user);
        }

        public void AddUser(string userId, ApplicationUser user)
        {
            if (userId != null && user != null)
            {
                _userCache.TryAdd(userId, user);
            }
        }

        public void ClearCache()
        {
            _userCache.Clear();
        }
    }
}