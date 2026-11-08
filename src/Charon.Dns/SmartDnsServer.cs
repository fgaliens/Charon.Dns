using System.Net;
using Charon.Dns.Interceptors;
using Charon.Dns.Lib.AsyncEvents;
using Charon.Dns.Lib.Server;
using Charon.Dns.RequestResolving;
using Charon.Dns.Settings;
using Charon.Dns.Utils;
using Charon.Dns.Zones;
using Serilog;

namespace Charon.Dns;

public class SmartDnsServer(
    ISmartRequestResolver smartRequestResolver,
    IRequestInterceptor requestInterceptor,
    IResponseInterceptor responseInterceptor,
    ListeningSettings listeningSettings,
    ZonesSettings zonesSettings,
    ILogger logger)
{
    private static readonly RequestCounter RequestCounter = new();
    
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    
    public async Task Start()
    {
        var cancellationToken = _cancellationTokenSource.Token;
        cancellationToken.ThrowIfCancellationRequested();
        
        var zoneResolver = ZoneResolverFactory.Create(zonesSettings);

        var requestResolvers = new DnsServer.FallbackRequestResolver(
            zoneResolver,
            smartRequestResolver);
        
        var server = new DnsServer(
            requestResolvers,
            RequestCounter,
            listeningSettings.MaxParallelRequestCount,
            listeningSettings.SocketBufferSize,
            logger,
            recursionAvailable: true);
        server.Subscribe(AsyncObserver.Create<OnExceptionEventArgs>(eventArgs =>
        {
            logger.Error(eventArgs.Exception, "Error occured");
            return Task.CompletedTask;
        }));

        server.Subscribe(requestInterceptor);
        server.Subscribe(responseInterceptor);

        var listeningTasks = new List<Task>();
        foreach (var listeningSettingsItem in listeningSettings.Items)
        {
            var endpoint = new IPEndPoint(listeningSettingsItem.Address, listeningSettingsItem.Port);
            var enableIpV6 = listeningSettingsItem.EnableIpV6;
            switch (listeningSettingsItem.Mode)
            {
                case TransportMode.UdpAndTcp:
                    // RFC 9210: TCP is served next to UDP on the same address and port
                    listeningTasks.Add(Listen("UDP", endpoint,
                        () => server.Listen(endpoint, enableIpV6, cancellationToken)));
                    listeningTasks.Add(Listen("TCP", endpoint,
                        () => server.ListenTcp(endpoint, enableIpV6, cancellationToken)));
                    break;
                case TransportMode.DoT:
                    var dotCertificate = listeningSettingsItem.Certificate!;
                    listeningTasks.Add(Listen("DoT", endpoint,
                        () => server.ListenTls(endpoint, enableIpV6, dotCertificate, cancellationToken)));
                    break;
                case TransportMode.DoH:
                    var dohCertificate = listeningSettingsItem.Certificate!;
                    var path = listeningSettingsItem.Path;
                    listeningTasks.Add(Listen("DoH", endpoint,
                        () => server.ListenHttps(endpoint, enableIpV6, dohCertificate, path, cancellationToken)));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(listeningSettings), listeningSettingsItem.Mode, "Unknown transport mode");
            }
        }

        await Task.WhenAll(listeningTasks);
    }

    private Task Listen(string transport, IPEndPoint endpoint, Func<Task> listen)
    {
        return Task.Run(async () =>
        {
            logger.Information("Listening on {EndPoint} ({Transport}).", endpoint, transport);
            try
            {
                await listen();
            }
            catch (OperationCanceledException) when (_cancellationTokenSource.IsCancellationRequested)
            {
                // Stopped
            }
            catch (Exception e)
            {
                logger.Error(e, "Error occured while listening on {EndPoint} ({Transport})", endpoint, transport);
            }

            logger.Warning("Stop listening on {EndPoint} ({Transport}).", endpoint, transport);
        }, CancellationToken.None);
    }

    public void Stop()
    {
        _cancellationTokenSource.Cancel();
    }
}
