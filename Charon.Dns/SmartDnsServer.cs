using System.Net;
using Charon.Dns.Interceptors;
using Charon.Dns.Lib.AsyncEvents;
using Charon.Dns.Lib.Server;
using Charon.Dns.RequestResolving;
using Charon.Dns.Settings;
using Charon.Dns.Utils;
using Serilog;

namespace Charon.Dns;

public class SmartDnsServer(
    ISmartRequestResolver smartRequestResolver,
    IResponseInterceptor responseInterceptor,
    ListeningSettings listeningSettings,
    DnsRecordsSettings dnsRecords,
    CacheSettings cacheSettings,
    ILogger logger)
{
    private static readonly RequestCounter RequestCounter = new();
    
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    
    public async Task Start()
    {
        var cancellationToken = _cancellationTokenSource.Token;
        cancellationToken.ThrowIfCancellationRequested();
        
        var masterFile = new MasterFile(cacheSettings.TimeToLive);
        foreach (var aRecord in dnsRecords.ARecords)
        {
            masterFile.AddIpAddressResourceRecord(
                aRecord.Name, 
                aRecord.Address,
                aRecord.ResolveOnlyIfRequestCameFrom);
        }

        var requestResolvers = new DnsServer.FallbackRequestResolver(
            masterFile,
            smartRequestResolver);
        
        var server = new DnsServer(
            requestResolvers, 
            RequestCounter, 
            listeningSettings.MaxParallelRequestCount, 
            listeningSettings.SocketBufferSize,
            logger);
        server.Subscribe(AsyncObserver.Create<OnExceptionEventArgs>(eventArgs =>
        {
            logger.Error(eventArgs.Exception, "Error occured");
            return Task.CompletedTask;
        }));

        server.Subscribe(responseInterceptor);

        var listeningTasks = new List<Task>();
        foreach (var listeningSettingsItem in listeningSettings.Items)
        {
#if !DEBUG
            if (listeningSettingsItem.DebugOnly)
            {
                continue;
            }
#endif
            
            var task = Task.Run(async () =>
            {
                logger.Information("Listening on {Ip}:{Port}.",
                    listeningSettingsItem.Address, listeningSettingsItem.Port);
                try
                {
                    await server.Listen(
                        new IPEndPoint(listeningSettingsItem.Address, listeningSettingsItem.Port), 
                        cancellationToken);
                }
                catch (Exception e)
                {
                    logger.Error(e, "Error occured while listening on {Ip}:{Port}", 
                        listeningSettingsItem.Address, listeningSettingsItem.Port);
                }
                
                logger.Warning("Stop listening on {Ip}:{Port}.",
                    listeningSettingsItem.Address, listeningSettingsItem.Port);
            }, cancellationToken);
            listeningTasks.Add(task);
        }

        await Task.WhenAll(listeningTasks);
    }
    
    public void Stop()
    {
        _cancellationTokenSource.Cancel();
    }
}
