using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Charon.Dns.Extensions;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Settings;
using Charon.Dns.Utils;
using Serilog;

namespace Charon.Dns.Cache;

public class DnsCache(
    IDateTimeProvider dateTimeProvider,
    CacheSettings cacheSettings,
    ILogger logger) 
    : IDnsCache
{
    private ImmutableSortedSet<CacheEntry> _cacheEntries = ImmutableSortedSet.Create<CacheEntry>(CacheEntryEqualityComparer.Instance);
    private ImmutableDictionary<IRequest, IResponse> _cache = ImmutableDictionary.Create<IRequest, IResponse>();
    
    public void AddResponse(IRequest request, IResponse response)
    {
        if (IsDisabled())
        {
            return;
        }
        
        if (response.AnswerRecords.Count == 0)
        {
            return;
        }

        if (ImmutableInterlocked.TryAdd(ref _cache, request, response))
        {
            ImmutableInterlockedUtils.Add(ref _cacheEntries, new()
            {
                ValidUntill = dateTimeProvider.UtcNow + cacheSettings.TimeToLive,
                Request = request,
            });
        }

        logger.Debug("Response added to cache for request {Request}", request);
    }

    public bool TryGetResponse(IRequest request, [NotNullWhen(true)] out IResponse? response)
    {
        response = null;
        
        if (IsDisabled())
        {
            return false;
        }

        if (!_cache.TryGetValue(request, out var cachedResponse))
        {
            return false;
        }
        
        logger.Debug("Cache hit for request {Request}: {Response}", request, cachedResponse);
        
        response = new Response(cachedResponse);
        response.Id = request.Id;
        return true;
    }

    public void RemoveOutdatedResponses()
    {
        if (IsDisabled())
        {
            return;
        }
        
        var cacheEntries = _cacheEntries;
        while (cacheEntries.Count > 0 && cacheEntries.Min.ValidUntill < dateTimeProvider.UtcNow)
        {
            logger.Debug("Removing outdated cache entry: {Entry}", cacheEntries.Min);
            ImmutableInterlocked.TryRemove(ref _cache, cacheEntries.Min.Request, out _);
            ImmutableInterlockedUtils.Remove(ref _cacheEntries, cacheEntries.Min);
            
            cacheEntries = _cacheEntries;
        }
    }

    private bool IsDisabled()
    {
        return cacheSettings.TimeToLive <= TimeSpan.Zero;
    }

    private readonly record struct CacheEntry
    {
        public required DateTimeOffset ValidUntill { get; init; }
        public required IRequest Request { get; init; }
    }

    private class CacheEntryEqualityComparer : IComparer<CacheEntry>
    {
        public static CacheEntryEqualityComparer Instance { get; } = new();
        public int Compare(CacheEntry x, CacheEntry y)
        {
            return x.ValidUntill.CompareTo(y.ValidUntill);
        }
    }
}
