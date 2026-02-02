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
                var shouldBeSecured = request.Questions.Any(x => hostNameAnalyzer.ShouldBeSecured(x.Name));
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
