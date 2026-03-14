using System.Diagnostics.CodeAnalysis;
using System.Net;
using Charon.Dns.Interceptors;
using Charon.Dns.Lib.AsyncEvents;
using Charon.Dns.Lib.Server;
using Charon.Dns.RequestResolving;
using Charon.Dns.Settings;
using Serilog;

namespace Charon.Dns
{
    public class SmartDnsServer(
        ISmartRequestResolver smartRequestResolver,
        ICachedRequestResolver cachedRequestResolver,
        IResponseInterceptor responseInterceptor,
        ListeningSettings listeningSettings,
        DnsRecordsSettings dnsRecords,
        ILogger logger)
    {
        public async Task Start()
        {
            var masterFile = new MasterFile();
            foreach (var aRecord in dnsRecords.ARecords)
            {
                masterFile.AddIPAddressResourceRecord(aRecord.Name, aRecord.Address);
            }

            var requestResolvers = new DnsServer.FallbackRequestResolver(
                masterFile,
                cachedRequestResolver,
                smartRequestResolver);
            
            using var server = new DnsServer(requestResolvers);
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
                
                var task = Task.Run([SuppressMessage("ReSharper", "AccessToDisposedClosure")] async () =>
                {
                    logger.Information("Listening on {Ip}:{Port}.",
                        listeningSettingsItem.Address, listeningSettingsItem.Port);
                    try
                    {
                        await server.Listen(new IPEndPoint(listeningSettingsItem.Address, listeningSettingsItem.Port));
                    }
                    catch (Exception e)
                    {
                        logger.Error(e, "Error occured while listening on {Ip}:{Port}", 
                            listeningSettingsItem.Address, listeningSettingsItem.Port);
                    }
                    
                    logger.Warning("Stop listening on {Ip}:{Port}.",
                        listeningSettingsItem.Address, listeningSettingsItem.Port);
                });
                listeningTasks.Add(task);
            }

            await Task.WhenAll(listeningTasks);
        }
    }
}
