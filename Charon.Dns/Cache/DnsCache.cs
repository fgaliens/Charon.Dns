using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Charon.Dns.Extensions;
using Charon.Dns.Lib.Extensions;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Tracing;
using Charon.Dns.Settings;
using Charon.Dns.Utils;
using Serilog;

namespace Charon.Dns.Cache;

// TODO: Implement returning Task<IResponse> for parallel requests
public class DnsCache(
    IDateTimeProvider dateTimeProvider,
    CacheSettings cacheSettings,
    ILogger globalLogger) 
    : IDnsCache
{
    private ImmutableSortedSet<CacheEntry> _cacheEntries = ImmutableSortedSet.Create<CacheEntry>(CacheEntryEqualityComparer.Instance);
    private ImmutableDictionary<IRequest, CacheEntry> _cache = ImmutableDictionary.Create<IRequest, CacheEntry>();
    
    public void AddResponse(
        IRequest request, 
        IResponse response, 
        RequestTrace trace)
    {
        if (IsDisabled())
        {
            return;
        }
        
        if (response.AnswerRecords.Count == 0)
        {
            return;
        }
        
        var logger = trace.Logger;

        var cacheTtl = response.AnswerRecords.Min(x => x.TimeToLive);
        cacheTtl = cacheTtl > TimeSpan.Zero ? cacheTtl : cacheSettings.TimeToLive;
        var validUntil = dateTimeProvider.UtcNow + cacheTtl;

        var responseEntry = new CacheEntry
        {
            ValidUntil = validUntil,
            Request = request,
            Response = response,
        };
            
        if (ImmutableInterlocked.TryAdd(ref _cache, request, responseEntry))
        {
            ImmutableInterlockedUtils.Add(ref _cacheEntries, responseEntry);
            
            logger.Debug("Response added to cache for request {@Request}", request);
        }
    }

    public bool TryGetResponse(
        IRequest request, 
        RequestTrace trace,
        [NotNullWhen(true)] out IResponse? response)
    {
        response = null;
        
        if (IsDisabled())
        {
            return false;
        }

        if (!_cache.TryGetValue(request, out var cachedResponseEntry))
        {
            return false;
        }
        
        var logger = trace.Logger;
        var cachedResponse = cachedResponseEntry.Response;
        var now = dateTimeProvider.UtcNow;

        if (cachedResponseEntry.ValidUntil < now)
        {
            RemoveCacheEntry(cachedResponseEntry);
            return false;
        }
        
        logger.Debug("Cache hit for request {@Request}: {@Response}", request, cachedResponse);

        var rawResponseMessage = cachedResponse.ToArray();
        var responseHeader = rawResponseMessage.AsDnsMessage.Header;
        responseHeader.Id = (ushort)request.Id;

        response = Response.FromArray(rawResponseMessage);
        return true;
    }

    public void RemoveOutdatedResponses()
    {
        if (IsDisabled())
        {
            return;
        }
        
        var cacheEntries = _cacheEntries;
        while (cacheEntries.Count > 0 && cacheEntries.Min.ValidUntil < dateTimeProvider.UtcNow)
        {
            var cacheEntry = cacheEntries.Min;
            globalLogger.Debug("Removing outdated cache entry. Valid until: {Valid}; Request: {@Request}; Response: {@Response}", 
                cacheEntry.ValidUntil, cacheEntry.Request, cacheEntry.Response);
            RemoveCacheEntry(cacheEntry);
            
            cacheEntries = _cacheEntries;
        }
    }

    private bool IsDisabled()
    {
        return !cacheSettings.Enabled;
    }

    private void RemoveCacheEntry(in CacheEntry entry)
    {
        ImmutableInterlocked.TryRemove(ref _cache, entry.Request, out _);
        ImmutableInterlockedUtils.Remove(ref _cacheEntries, entry);
    }

    private readonly record struct CacheEntry
    {
        public required DateTimeOffset ValidUntil { get; init; }
        public required IRequest Request { get; init; }
        public required IResponse Response { get; init; }
    }

    private class CacheEntryEqualityComparer : IComparer<CacheEntry>
    {
        public static CacheEntryEqualityComparer Instance { get; } = new();
        public int Compare(CacheEntry x, CacheEntry y)
        {
            return x.ValidUntil.CompareTo(y.ValidUntil);
        }
    }
}
