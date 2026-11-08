using System.Diagnostics.CodeAnalysis;
using Charon.Dns.Lib.Protocol;

namespace Charon.Dns.Cache;

public interface IDnsCache
{
    void AddResponse(
        IReadOnlyRequest request,
        IReadOnlyResponse response);
    bool TryGetResponse(
        IReadOnlyRequest request,
        [NotNullWhen(true)] out IResponse? response);
    void RemoveOutdatedResponses();
}
