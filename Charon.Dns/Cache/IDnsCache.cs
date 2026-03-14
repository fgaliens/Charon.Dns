using System.Diagnostics.CodeAnalysis;
using Charon.Dns.Lib.Protocol;

namespace Charon.Dns.Cache;

public interface IDnsCache
{
    void AddResponse(IRequest request, IResponse response);
    bool TryGetResponse(IRequest request, [NotNullWhen(true)] out IResponse? response);
    void RemoveOutdatedResponses();
}