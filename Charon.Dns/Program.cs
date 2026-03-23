using System.Runtime.CompilerServices;
using Charon.Dns.Cache;
using Charon.Dns.Extensions;
using Charon.Dns.Interceptors;
using Charon.Dns.Jobs;
using Charon.Dns.Jobs.Implementations;
using Charon.Dns.Logging;
using Charon.Dns.RequestResolving;
using Charon.Dns.Routing;
using Charon.Dns.Settings;
using Charon.Dns.SystemCommands;
using Charon.Dns.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

[assembly: InternalsVisibleTo("Charon.Dns.Tests")]
[assembly: InternalsVisibleTo("Charon.Dns.EndToEndTests")]

namespace Charon.Dns;

static class Program
{
    private const string AppVersion = "1.5.2";

    public async static Task Main(string[] args)
    {
        await Main(args, default);
    }
    
    public async static Task Main(string[] args, CancellationToken cancellationToken)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("settings.json")
            .AddCommandLine(args)
            .Build();
        
        ConsoleTheme consoleTheme =
#if DEBUG
            AnsiConsoleTheme.Code; 
#else
            ConsoleTheme.None;
#endif

        var logger = new LoggerConfiguration()
            .MinimumLevel.Is(LogEventLevel.Debug)
            .Destructure.With(new LoggingDestructuringPolicies())
            .WriteTo.Console(
                restrictedToMinimumLevel: GetConsoleLogLevel(),
                theme: consoleTheme)
            .WriteTo.File(
                "logs/dns.log", 
                GetFileLogLevel(),
                buffered: true,
                flushToDiskInterval: TimeSpan.FromSeconds(5),
                rollingInterval: RollingInterval.Minute,
                retainedFileCountLimit: 60)
            .CreateLogger();

        logger.Information("Starting up DNS server. Version {AppVersion}", AppVersion);

        var serviceProvider = new ServiceCollection()
            .AddSingleton<ServiceInitializer>()
            .AddSingleton<SmartDnsServer>()
            .AddSingleton<IHostNameAnalyzer, HostNameAnalyzer>()
            .AddSingleton<IDnsCache, DnsCache>()
            .AddSingleton<ISmartRequestResolver, SmartRequestResolver>()
            .AddSingleton<IDefaultRequestResolver, DefaultRequestResolver>()
            .AddSingleton<ISafeRequestResolver, SafeRequestResolver>()
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

        cancellationToken.Register(() => smartDnsServer.Stop());
        
        await serviceInitializer.Initialize();

        await smartDnsServer.Start();
        LogEventLevel GetConsoleLogLevel()
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
        
        LogEventLevel GetFileLogLevel()
        {
            if (Enum.TryParse<LogEventLevel>(config["FileLogLevel"], out var logLevel))
            {
                return logLevel;
            }

            return LogEventLevel.Debug;
        }
    }
}