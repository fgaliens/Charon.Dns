using Microsoft.Extensions.DependencyInjection;

namespace Charon.Dns.AccessControl;

public static class AccessControlDiExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddUserAccessControl()
        {
            return services
                .AddSingleton<IClientActivityTracker, ClientActivityTracker>()
                .AddSingleton<IUserAccessControlManager, UserAccessControlManager>();
        }
    }
}
