using System.Diagnostics;
using System.Net;
using Charon.Dns.Cache;
using Charon.Dns.Lib.Protocol;
using Serilog;
using Serilog.Events;

namespace Charon.Dns.RequestResolving
{
    public class SmartRequestResolver(
        IDefaultRequestResolver defaultRequestResolver,
        ISafeRequestResolver safeRequestResolver,
        IHostNameAnalyzer hostNameAnalyzer,
        IDnsCache  dnsCache,
        ILogger logger) : ISmartRequestResolver
    {
        public async Task<IResponse> Resolve(IRequest request, IPEndPoint remoteEndPoint, CancellationToken cancellationToken = default)
        {
            var response = await ResolveInternal(request, remoteEndPoint, cancellationToken);
            dnsCache.AddResponse(request, response);
            return response;
        }
        
        private async Task<IResponse> ResolveInternal(IRequest request, IPEndPoint remoteEndPoint, CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                var shouldBeBlocked = false;
                var shouldBeSecured = false;
                foreach (var question in request.Questions)
                {
                    var hostName = question.Name.ToString();
                    
                    if (hostNameAnalyzer.ShouldBeBlocked(hostName))
                    {
                        shouldBeBlocked = true;
                        break;
                    }
                    
                    if (hostNameAnalyzer.ShouldBeSecured(hostName))
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
                    return await safeRequestResolver.Resolve(request, remoteEndPoint, cancellationToken);
                }

                logger.Debug("Dns request resolving is non secured ({@Request})", request);
                return await defaultRequestResolver.Resolve(request, remoteEndPoint, cancellationToken);
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
