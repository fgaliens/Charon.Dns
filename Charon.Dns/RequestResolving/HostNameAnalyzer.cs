using System.Collections.Frozen;
using Charon.Dns.Extensions;
using Charon.Dns.Settings;
using Serilog;

namespace Charon.Dns.RequestResolving
{
    public class HostNameAnalyzer(
        RoutingSettings routingSettings,
        ILogger logger) 
        : IHostNameAnalyzer
    {
        private readonly FrozenSet<string> _domainMatchedHostnames = 
            routingSettings.MatchedByDomainHostNames.ToFrozenSet(DomainNameComparer.Instance);
        
        private readonly FrozenSet<string> _blockedHostnames = 
            routingSettings.BlockedHostNames.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        public bool ShouldBeSecured(string domainName)
        {
            var result = ShouldBeSecuredInternal(domainName);
            logger.Debug("Host name '{Host}' should be secured: {IsSecured}", domainName, result);
            return result;
        }

        public bool ShouldBeBlocked(string domainName)
        {
            return _blockedHostnames.Contains(domainName);
        }

        private bool ShouldBeSecuredInternal(string domainName)
        {
            if (_domainMatchedHostnames.Contains(domainName))
            {
                logger.Debug("Host name '{Host}' should be secured because it is matched by domain", domainName);
                
                return true;
            }

            foreach (var hostNameSubstring in routingSettings.MatchedBySubstringHostNames)
            {
                if (domainName.Contains(hostNameSubstring, StringComparison.OrdinalIgnoreCase))
                {
                    logger.Debug("Host name '{Host}' should be secured because it contains {Substring}", 
                        domainName, hostNameSubstring);
                    return true;
                }
            }

            return false;
        }
    }
}
