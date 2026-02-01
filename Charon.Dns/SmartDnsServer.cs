using System.Diagnostics.CodeAnalysis;
using System.Net;
using Charon.Dns.Interceptors;
using Charon.Dns.Lib.Server;
using Charon.Dns.RequestResolving;
using Charon.Dns.Settings;
using Serilog;

namespace Charon.Dns
{
    public class SmartDnsServer(
        ISmartRequestResolver smartRequestResolver,
        IRequestInterceptor requestInterceptor,
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
                smartRequestResolver);
            
            using var server = new DnsServer(requestResolvers);
            server.Errored += (_, eventArgs) =>
            {
                logger.Error(eventArgs.Exception, "Error occured");
            };

            server.Responded += (_, eventArgs) =>
            {
                requestInterceptor.Handle(eventArgs.Request, eventArgs.Response, CancellationToken.None);
            };

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
