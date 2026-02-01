using Charon.Dns.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Charon.Dns.Extensions;

public static class SettingsExtensions
{
    public static IServiceCollection AddSettings<TSettings>(this IServiceCollection services) 
        where TSettings : class, ISettings<TSettings>
    {
        return services.AddSingleton(serviceProvider =>
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            return TSettings.Initialize(configuration);
        });
    }
}