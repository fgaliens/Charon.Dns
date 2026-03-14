using Charon.Dns;
using Charon.Dns.Cache;
using Charon.Dns.Extensions;
using Charon.Dns.Interceptors;
using Charon.Dns.Jobs;
using Charon.Dns.Jobs.Implementations;
using Charon.Dns.RequestResolving;
using Charon.Dns.Routing;
using Charon.Dns.Settings;
using Charon.Dns.SystemCommands;
using Charon.Dns.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;

const string appVersion = "1.4";

var config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("settings.json")
    .AddCommandLine(args)
    .Build();

var logger = new LoggerConfiguration()
    .WriteTo.Console()
    .MinimumLevel.Is(GetLogLevel())
    .CreateLogger();

logger.Information("Starting up DNS server. Version {AppVersion}", appVersion);

var serviceProvider = new ServiceCollection()
    .AddSingleton<ServiceInitializer>()
    .AddSingleton<SmartDnsServer>()
    .AddSingleton<IHostNameAnalyzer, HostNameAnalyzer>()
    .AddSingleton<IDnsCache, DnsCache>()
    .AddSingleton<ISmartRequestResolver, SmartRequestResolver>()
    .AddSingleton<IDefaultRequestResolver, DefaultRequestResolver>()
    .AddSingleton<ISafeRequestResolver, SafeRequestResolver>()
    .AddSingleton<ICachedRequestResolver, CachedRequestResolver>()
    .AddSingleton<ICommandRunner, CommandRunner>()
    .AddSingleton<IResponseInterceptor, ResponseInterceptor>()
    .AddRouteManagement()
    .AddJobs(cfg => cfg
        .AddJob<RemoveOutdatedRoutesJob>()
        .AddJob<RemoveOutdatedCacheEntriesJob>())
    .AddSingleton<IConfiguration>(config)
    .AddSettings<ListeningSettings>()
    .AddSettings<DnsRecordsSettings>()
    .AddSettings<DnsChainSettings>()
    .AddSettings<RoutingSettings>()
    .AddSettings<CacheSettings>()
    .AddSingleton<IDateTimeProvider, DateTimeProvider>()
    .AddSingleton<ILogger>(logger)
    .BuildServiceProvider();

var serviceInitializer = serviceProvider.GetRequiredService<ServiceInitializer>();
var smartDnsServer = serviceProvider.GetRequiredService<SmartDnsServer>();

await serviceInitializer.Initialize();

await smartDnsServer.Start();
LogEventLevel GetLogLevel()
{
#if DEBUG
    return LogEventLevel.Debug;
#else
    if (Enum.TryParse<LogEventLevel>(config["LogLevel"], out var logLevel))
    {
        return logLevel;
    }

    return LogEventLevel.Information;
#endif
}