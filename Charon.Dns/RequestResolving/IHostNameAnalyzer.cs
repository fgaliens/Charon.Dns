using System.Diagnostics.CodeAnalysis;
using Charon.Dns.Lib.Protocol;

namespace Charon.Dns.RequestResolving
{
    public interface IHostNameAnalyzer
    {
        bool ShouldBeSecured(string domainName);
        bool ShouldBeSecured(string domainName, [NotNullWhen(true)] out SecuredConnectionParams? connectionParams);
        bool ShouldBeBlocked(string domainName);
    }
}
