using System.Diagnostics.CodeAnalysis;
using Charon.Dns.Lib.AsyncEvents;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Protocol.ResourceRecords;
using Charon.Dns.Lib.Tracing;
using Charon.Dns.Net;
using Charon.Dns.RequestResolving;
using Charon.Dns.Routing;
using Serilog;

namespace Charon.Dns.Interceptors;

public class ResponseInterceptor(
    IHostNameAnalyzer hostNameAnalyzer,
    IRouteManager<IpV4Network> ipV4NetworkManager,
    IRouteManager<IpV6Network> ipV6NetworkManager) 
    : IResponseInterceptor
{
    public async Task Handle(
        IRequest request, 
        IResponse response,
        RequestTrace trace,
        CancellationToken token = default)
    {
        var logger = trace.Logger;

        if (response.Truncated)
        {
            logger.Warning("Response {@Response} for request {@Request} has been truncated", response, request);
            return;
        }

        SecuredConnectionParams? connectionParams = null;

        var shouldBeSecured = request.Questions.Any(x => hostNameAnalyzer.ShouldBeSecured(
            x.Name.ToString(),
            trace,
            out connectionParams));

        if (!shouldBeSecured)
        {
            return;
        }

        if (connectionParams is null)
        {
            logger.Error("Unable to get connection parameters for request {@Request}", request);
            throw new NullReferenceException("Connection parameters can't be null");
        }
        
        foreach (var answer in response.AnswerRecords)
        {
            if (answer.TryCastTo<CanonicalNameResourceRecord>(logger, RecordType.CNAME, out var canonicalNameRecord))
            { 
                hostNameAnalyzer.AddSecuredDomainName(canonicalNameRecord.CanonicalDomainName.ToString(), connectionParams, trace);
            }
            else if (answer.TryCastTo<MailExchangeResourceRecord>(logger, RecordType.MX, out var mailExchangeRecord))
            { 
                hostNameAnalyzer.AddSecuredDomainName(mailExchangeRecord.ExchangeDomainName.ToString(), connectionParams, trace);
            }
            else if (answer.TryCastTo<NameServerResourceRecord>(logger, RecordType.NS, out var nameServerRecord))
            { 
                hostNameAnalyzer.AddSecuredDomainName(nameServerRecord.NSDomainName.ToString(), connectionParams, trace);
            }
            else if (answer.TryCastTo<PointerResourceRecord>(logger, RecordType.PTR, out var pointerRecord))
            { 
                hostNameAnalyzer.AddSecuredDomainName(pointerRecord.PointerDomainName.ToString(), connectionParams, trace);
            }
            else if (answer.TryCastTo<ServiceResourceRecord>(logger, RecordType.SRV, out var serviceRecord))
            { 
                hostNameAnalyzer.AddSecuredDomainName(serviceRecord.Target.ToString(), connectionParams, trace);
            }
            else if (answer.TryCastTo<StartOfAuthorityResourceRecord>(logger, RecordType.SOA, out var startOfAuthorityRecord))
            { 
                hostNameAnalyzer.AddSecuredDomainName(startOfAuthorityRecord.MasterDomainName.ToString(), connectionParams, trace);
                hostNameAnalyzer.AddSecuredDomainName(startOfAuthorityRecord.ResponsibleDomainName.ToString(), connectionParams, trace);
            }
        }

        var addRouteTasks = new List<Task>();
        foreach (var answer in response.AnswerRecords)
        {
            if (answer.Type is RecordType.A)
            {
                var ipV4Network = new IpV4Network(answer.Data, connectionParams!.IpV4RoutingSubnet);
                var addRouteTask = ipV4NetworkManager.AddRoute(
                    ipV4Network,
                    connectionParams.InterfaceName,
                    trace);
                addRouteTasks.Add(addRouteTask);
            }
            else if (answer.Type is RecordType.AAAA)
            {
                var ipV6Network = new IpV6Network(answer.Data, connectionParams!.IpV6RoutingSubnet);
                var addRouteTask = ipV6NetworkManager.AddRoute(
                    ipV6Network,
                    connectionParams.InterfaceName,
                    trace);
                addRouteTasks.Add(addRouteTask);
            }
        }

        await Task.WhenAll(addRouteTasks);
    }

    async Task IAsyncObserver<OnResponseEventArgs>.OnEvent(OnResponseEventArgs eventArgs)
    {
        await Handle(eventArgs.Request, eventArgs.Response, eventArgs.Trace);
    }

    Task IAsyncObserver<OnResponseEventArgs>.OnCompleted()
    {
        return Task.CompletedTask;
    }
}

file static class ResourceRecordExtensions
{
    extension(IResourceRecord resourceRecord)
    {
        public bool TryCastTo<T>(ILogger logger, RecordType type, [NotNullWhen(true)] out T? value)
            where T : class, IResourceRecord
        {
            if (resourceRecord.Type == type)
            {
                if (resourceRecord is T castedValue)
                {
                    value = castedValue;
                    return true;
                }
                
                var typeName = typeof(T).Name;
                logger.Warning("Unable to cast answer with type {Type} to {Resource}. It's type: {OriginalType}",
                    resourceRecord.Type, typeName, resourceRecord.GetType());
            }
            
            value = null;
            return false;
        }
    }
}
