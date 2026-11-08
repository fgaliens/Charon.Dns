using Charon.Dns.RequestResolving.ResolvingStrategies;
using Charon.Dns.Settings;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Charon.Dns.RequestResolving;

public static class RequestResolvingExtensions
{
    extension (IServiceCollection services)
    {
        public IServiceCollection AddRequestResolving()
        {
            const string strategyCollection = "resolving-strategy-collection";
            services
                .AddSingleton<ISmartRequestResolver, SmartRequestResolver>()
                .AddSingleton<IDefaultRequestResolver, DefaultRequestResolver>()
                .AddSingleton<ISafeRequestResolver, SafeRequestResolver>()
                .AddKeyedSingleton<IResolvingStrategy, RoundRobinResolvingStrategy>(strategyCollection)
                .AddKeyedSingleton<IResolvingStrategy, RandomResolvingStrategy>(strategyCollection)
                .AddKeyedSingleton<IResolvingStrategy, ParallelResolvingStrategy>(strategyCollection)
                .AddTransient(serviceProvider =>
                {
                    var logger = serviceProvider.GetRequiredService<ILogger>();
                    try
                    {
                        var dnsChainSettings = serviceProvider.GetRequiredService<DnsChainSettings>();
                        var resolvingStrategies = serviceProvider.GetKeyedServices<IResolvingStrategy>(strategyCollection);
                        var selectedStrategy = resolvingStrategies.Single(x => x.Strategy == dnsChainSettings.ResolvingStrategy);
                        
                        logger.Debug("Request resolving strategy is '{Strategy}'", selectedStrategy.Strategy);
                        
                        return selectedStrategy;
                    }
                    catch (Exception e)
                    {
                        logger.Error(e, "Error while selecting strategy");
                        throw;
                    }
                });
            
            return services;
        }
    }
}
