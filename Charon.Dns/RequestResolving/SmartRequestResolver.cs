using System.Diagnostics;
using Charon.Dns.Lib.Protocol;
using Serilog;

namespace Charon.Dns.RequestResolving
{
    public class SmartRequestResolver(
        IDefaultRequestResolver defaultRequestResolver,
        ISafeRequestResolver safeRequestResolver,
        IHostNameAnalyzer hostNameAnalyzer,
        ILogger logger) : ISmartRequestResolver
    {
        public async Task<IResponse> Resolve(IRequest request, CancellationToken cancellationToken = default(CancellationToken))
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
                    logger.Information("Dns request was blocked ({Request})", request);
                    return Response.FromRequest(request);
                }
                
                if (shouldBeSecured)
                {
                    logger.Information("Dns request resolving is secured ({Request})", request);
                    return await safeRequestResolver.Resolve(request, cancellationToken);
                }

                logger.Debug("Dns request resolving is non secured ({Request})", request);
                return await defaultRequestResolver.Resolve(request, cancellationToken);
            }
            catch (Exception e)
            {
                logger.Error(e, "Dns request resolving failed ({Request})", request);
                throw;
            }
            finally
            {
                logger.Debug("Request handled in {Time} ms.", stopwatch.ElapsedMilliseconds);
            }
        }
    }
}
