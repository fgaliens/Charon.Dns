using Charon.Dns;
using Charon.Dns.Extensions;
using Charon.Dns.Interceptors;
using Charon.Dns.RequestResolving;
using Charon.Dns.Settings;
using Charon.Dns.SystemCommands;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;

var config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("settings.json")
    .AddCommandLine(args)
    .Build();

var logger = new LoggerConfiguration()
    .WriteTo.Console()
    .MinimumLevel.Is(GetLogLevel())
    .CreateLogger();

var serviceProvider = new ServiceCollection()
    .AddSingleton<ServiceInitializer>()
    .AddSingleton<SmartDnsServer>()
    .AddSingleton<IHostNameAnalyzer, HostNameAnalyzer>()
    .AddSingleton<ISmartRequestResolver, SmartRequestResolver>()
    .AddSingleton<IDefaultRequestResolver,  DefaultRequestResolver>()
    .AddSingleton<ISafeRequestResolver,  SafeRequestResolver>()
    .AddSingleton<ICommandRunner, CommandRunner>()
    .AddSingleton<IRequestInterceptor, RequestInterceptor>()
    .AddSingleton<IConfiguration>(config)
    .AddSettings<ListeningSettings>()
    .AddSettings<DnsRecordsSettings>()
    .AddSettings<DnsChainSettings>()
    .AddSettings<RoutingSettings>()
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