using System.Diagnostics;
using Charon.Dns.Cache;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Tracing;
using Serilog.Events;

namespace Charon.Dns.RequestResolving
{
    public class SmartRequestResolver(
        IDefaultRequestResolver defaultRequestResolver,
        ISafeRequestResolver safeRequestResolver,
        IHostNameAnalyzer hostNameAnalyzer,
        IDnsCache dnsCache) : ISmartRequestResolver
    {
        public async Task<IResponse> Resolve(
            IRequest request, 
            RequestTrace trace,
            CancellationToken cancellationToken = default)
        {
            if (dnsCache.TryGetResponse(request, trace, out var cachedResponse))
            {
                return cachedResponse;
            }
            
            var response = await ResolveInternal(request, trace, cancellationToken);
            dnsCache.AddResponse(request, response, trace);
            
            trace.Logger.Debug("Got response {@Response}", response);
            
            return response;
        }
        
        private async Task<IResponse> ResolveInternal(
            IRequest request, 
            RequestTrace trace, 
            CancellationToken cancellationToken)
        {
            var logger = trace.Logger;
            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                var shouldBeBlocked = false;
                var shouldBeSecured = false;
                foreach (var question in request.Questions)
                {
                    var hostName = question.Name.ToString();
                    
                    if (hostNameAnalyzer.ShouldBeBlocked(hostName, trace))
                    {
                        shouldBeBlocked = true;
                        break;
                    }
                    
                    if (hostNameAnalyzer.ShouldBeSecured(hostName, trace))
                    {
                        shouldBeSecured = true;
                        break;
                    }
                }

                if (shouldBeBlocked)
                {
                    logger.Information("Dns request was blocked ({@Request})", request);
                    return Response.FromRequest(request);
                }
                
                if (shouldBeSecured)
                {
                    logger.Information("Dns request resolving is secured ({@Request})", request);
                    return await safeRequestResolver.Resolve(request, trace, cancellationToken);
                }

                logger.Debug("Dns request resolving is non secured ({@Request})", request);
                return await defaultRequestResolver.Resolve(request, trace, cancellationToken);
            }
            catch (Exception e)
            {
                logger.Error(e, "Dns request resolving failed ({@Request})", request);
                throw;
            }
            finally
            {
                var logLevel = stopwatch.ElapsedMilliseconds < 400 ? LogEventLevel.Debug : LogEventLevel.Warning;
                logger.Write(logLevel, "Request handled {@Request} in {Time} ms.", request, stopwatch.ElapsedMilliseconds);
            }
        }
    }
}
