using System.Diagnostics.CodeAnalysis;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Tracing;

namespace Charon.Dns.RequestResolving
{
    public interface IHostNameAnalyzer
    {
        bool ShouldBeSecured(string domainName, RequestTrace trace);
        bool ShouldBeSecured(
            string domainName,
            RequestTrace trace, 
            [NotNullWhen(true)] out SecuredConnectionParams? connectionParams);
        bool ShouldBeBlocked(string domainName, RequestTrace trace);
        void AddSecuredDomainName(string domainName, SecuredConnectionParams connectionParams, RequestTrace trace);
    }
}
