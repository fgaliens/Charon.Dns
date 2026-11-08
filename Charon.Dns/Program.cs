using System.CommandLine;
using System.Reflection;
using System.Runtime.CompilerServices;
using Charon.Dns.AccessControl;
using Charon.Dns.Cache;
using Charon.Dns.Configuration;
using Charon.Dns.Configuration.Cli;
using Charon.Dns.Configuration.Migrations;
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

[assembly: AssemblyInformationalVersion("2.0.0")]
[assembly: InternalsVisibleTo("Charon.Dns.Tests")]
[assembly: InternalsVisibleTo("Charon.Dns.EndToEndTests")]

namespace Charon.Dns;

static class Program
{
    private static readonly string AppVersion = Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
        .InformationalVersion;

    private static readonly ConfigFileNames ConfigFileNames =
#if DEBUG
        ConfigFileNames.Debug;
#else
        ConfigFileNames.Production;
#endif

    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("Charon.Dns - a DNS server with dynamic traffic routing")
        {
            TreatUnmatchedTokensAsErrors = false,
        };

        rootCommand.Options.OfType<VersionOption>().Single().Aliases.Add("-v");

        var settingsCommand = SettingsCommand.Create(ConfigFileNames);
        rootCommand.Subcommands.Add(settingsCommand);
        rootCommand.SetAction((_, _) => RunServerAsync(args));
        return await rootCommand.Parse(args).InvokeAsync();
    }

    private static async Task<int> RunServerAsync(string[] args)
    {
        var configDirectory = Directory.GetCurrentDirectory();

        MigrationResult migrationResult;
        try
        {
            migrationResult = ConfigMigrator.Default.MigrateFiles(configDirectory, ConfigFileNames);
        }
        catch (Exception e)
        {
            await Console.Error.WriteLineAsync($"Unable to migrate configuration files: {e.Message}");
            return 1;
        }

        var config = new ConfigurationBuilder()
            .SetBasePath(configDirectory)
            .AddJsonFile(ConfigFileNames.Settings)
#if DEBUG
            .AddJsonFile(ConfigFileNames.Zones)
            .AddJsonFile(ConfigFileNames.Routing)
#else
            // Installations upgraded from 1.x may have neither of them
            .AddJsonFile(ConfigFileNames.Zones, optional: true)
            .AddJsonFile(ConfigFileNames.Routing, optional: true)
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
        LogMigrationResult(logger, migrationResult);

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
                .AddSettings<ZonesSettings>()
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

        return 0;

        LogEventLevel GetConsoleLogLevel()
        {
            if (Enum.TryParse<LogEventLevel>(config["Logging:Console"], out var logLevel))
            {
                return logLevel;
            }

            return LogEventLevel.Information;
        }
        
        LogEventLevel GetFileLogLevel()
        {
            if (Enum.TryParse<LogEventLevel>(config["Logging:File"], out var logLevel))
            {
                return logLevel;
            }

            return LogEventLevel.Debug;
        }
    }

    private static void LogMigrationResult(ILogger logger, MigrationResult migrationResult)
    {
        if (!migrationResult.IsMigrated)
        {
            return;
        }

        logger.Information("Configuration files migrated from version {FromVersion} to {ToVersion}",
            migrationResult.FromVersion, migrationResult.ToVersion);

        foreach (var note in migrationResult.Notes)
        {
            logger.Information("Configuration migration: {Note}", note);
        }

        foreach (var backupFile in migrationResult.BackupFiles)
        {
            logger.Information("Configuration backup saved to {BackupFile}", backupFile);
        }
    }
}