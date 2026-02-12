using Charon.Dns.Lib.Protocol;

namespace Charon.Dns.RequestResolving
{
    public interface IHostNameAnalyzer
    {
        bool ShouldBeSecured(string domainName);
        bool ShouldBeBlocked(string domainName);
    }
}
