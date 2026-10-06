using System.CommandLine;
using System.Reflection;
using System.Runtime.CompilerServices;
using Charon.Dns.AccessControl;
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

[assembly: AssemblyInformationalVersion("1.6.2")]
[assembly: InternalsVisibleTo("Charon.Dns.Tests")]
[assembly: InternalsVisibleTo("Charon.Dns.EndToEndTests")]

namespace Charon.Dns;

static class Program
{
    private static readonly string AppVersion = Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
        .InformationalVersion;

    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("Charon.Dns - a lightweight DNS server")
        {
            TreatUnmatchedTokensAsErrors = false,
        };

        rootCommand.Options.OfType<VersionOption>().Single().Aliases.Add("-v");

        rootCommand.SetAction((_, _) => RunServerAsync(args));

        return await rootCommand.Parse(args).InvokeAsync();
    }

    private static async Task RunServerAsync(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
#if DEBUG
            .AddJsonFile("settings.debug.json")
#else
            .AddJsonFile("settings.json")
#endif
            .AddCommandLine(args)
            .Build();
        
        ConsoleTheme consoleTheme =
#if DEBUG
            AnsiConsoleTheme.Code; 
#else
            ConsoleTheme.None;
#endif

        await using var logger = new LoggerConfiguration()
            .MinimumLevel.Is(LogEventLevel.Debug)
            .Enrich.FromLogContext()
            .Destructure.With(new LoggingDestructuringPolicies())
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss.fff}][{Level:u3}][#{RequestId}] {Message:lj}{NewLine}{Exception}",
                restrictedToMinimumLevel: GetConsoleLogLevel(),
                theme: consoleTheme)
            .WriteTo.File(
                "logs/dns.log", 
                GetFileLogLevel(),
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}][{Level:u3}][#{RequestId}] {Message:lj}{NewLine}{Exception}",
                rollingInterval: RollingInterval.Minute,
                retainedFileCountLimit: 60,
                buffered: true, 
                flushToDiskInterval: TimeSpan.FromSeconds(1))
            .CreateLogger();

        logger.Information("Starting up DNS server. Version {AppVersion}", AppVersion);

        try
        {
            var serviceProvider = new ServiceCollection()
                .AddSingleton<ServiceInitializer>()
                .AddSingleton<SmartDnsServer>()
                .AddSingleton<IHostNameAnalyzer, HostNameAnalyzer>()
                .AddSingleton<IDnsCache, DnsCache>()
                .AddRequestResolving()
                .AddSingleton<ICommandRunner, CommandRunner>()
                .AddSingleton<IResponseInterceptor, ResponseInterceptor>()
                .AddSingleton<IRequestInterceptor, RequestInterceptor>()
                .AddRouteManagement()
                .AddUserAccessControl()
                .AddJobs(cfg => cfg
                    .AddJob<RemoveOutdatedRoutesJob>()
                    .AddJob<RemoveOutdatedCacheEntriesJob>()
                    .AddJob<EnforceUserAccessControlJob>())
                .AddSingleton<IConfiguration>(config)
                .AddSettings<ListeningSettings>()
                .AddSettings<DnsRecordsSettings>()
                .AddSettings<DnsChainSettings>()
                .AddSettings<RoutingSettings>()
                .AddSettings<CacheSettings>()
                .AddSettings<UserAccessControlSettings>()
                .AddSingleton<IDateTimeProvider, DateTimeProvider>()
                .AddSingleton<ILogger>(logger)
                .BuildServiceProvider();

            var serviceInitializer = serviceProvider.GetRequiredService<ServiceInitializer>();
            var smartDnsServer = serviceProvider.GetRequiredService<SmartDnsServer>();

            await serviceInitializer.Initialize();

            await smartDnsServer.Start();
        }
        catch (Exception e)
        {
            logger.Fatal(e, "Unhandled exception. Dns server stopped");
        }

        LogEventLevel GetConsoleLogLevel()
        {
            if (Enum.TryParse<LogEventLevel>(config["LogLevel"], out var logLevel))
            {
                return logLevel;
            }

            return LogEventLevel.Information;
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