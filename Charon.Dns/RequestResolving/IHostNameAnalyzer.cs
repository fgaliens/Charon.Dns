using Charon.Dns.Lib.Protocol;

namespace Charon.Dns.RequestResolving
{
    public interface IHostNameAnalyzer
    {
        bool ShouldBeSecured(Domain domain);
    }
}
