using System.Collections.Frozen;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Settings;
using Serilog;

namespace Charon.Dns.RequestResolving
{
    public class HostNameAnalyzer(
        RoutingSettings routingSettings,
        ILogger logger) : IHostNameAnalyzer
    {
        private readonly FrozenSet<string> _fullyMatchedHostnames = 
            routingSettings.FullyMatchedHostNames.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        public bool ShouldBeSecured(Domain domain)
        {
            var result = ShouldBeSecuredInternal(domain);
            logger.Debug("Host name '{Host}' should be secured: {IsSecured}", domain, result);
            return result;
        }
        
        private bool ShouldBeSecuredInternal(Domain domain)
        {
            var domainAsString = domain.ToString();

            if (_fullyMatchedHostnames.Contains(domainAsString))
            {
                return true;
            }

            return routingSettings.MatchedBySubstringHostNames.Any(
                hostNameSubstring => domainAsString.Contains(hostNameSubstring, StringComparison.OrdinalIgnoreCase));
        }
    }
}
