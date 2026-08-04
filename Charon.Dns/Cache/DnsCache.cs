using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Charon.Dns.Lib.Protocol;
using Charon.Dns.Lib.Protocol.ResourceRecords;
using Charon.Dns.Lib.Tracing;
using Charon.Dns.Settings;
using Charon.Dns.Utils;
using Serilog;

namespace Charon.Dns.Cache;

public class DnsCache(
    IDateTimeProvider dateTimeProvider,
    CacheSettings cacheSettings,
    ILogger globalLogger) 
    : IDnsCache
{
    private ConcurrentDictionary<IRequest, CacheEntry> _cache = new();
    
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
            
        if (_cache.TryAdd(request, responseEntry))
        {            
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
            _cache.Remove(cachedResponseEntry.Request, out _);
            return false;
        }
        
        logger.Debug("Cache hit for request {@Request}: {@Response}", request, cachedResponse);
        
        var cachedAnswers = cachedResponse.AnswerRecords.ToArray();
        response = new Response(cachedResponse);
        response.Id = request.Id;
        response.AnswerRecords.Clear();
        
        var ttl = cachedResponseEntry.ValidUntil - now;
        ttl = ttl >= TimeSpan.Zero ? ttl : TimeSpan.Zero;
        
        foreach (var answer in cachedAnswers)
        {
            if (answer is BaseResourceRecord resourceRecord)
            {
                var updatedResourceRecord = new ResourceRecord(
                    resourceRecord.Name,
                    resourceRecord.Data,
                    resourceRecord.Type,
                    resourceRecord.Class,
                    ttl);
                
                response.AnswerRecords.Add(updatedResourceRecord);
            }
            else
            {
                response.AnswerRecords.Add(answer);
            }
        }
        
        return true;
    }

    public void RemoveOutdatedResponses()
    {
        if (IsDisabled())
        {
            return;
        }

        var now = dateTimeProvider.UtcNow;
        var itemsToRemove = _cache
            .Where(x => x.Value.ValidUntil < now)
            .ToArray();

        foreach (var itemToRemove in itemsToRemove)
        {
            _cache.Remove(itemToRemove.Key, out _);
        }
    }

    private bool IsDisabled()
    {
        return !cacheSettings.Enabled;
    }

    private readonly record struct CacheEntry
    {
        public required DateTimeOffset ValidUntil { get; init; }
        public required IRequest Request { get; init; }
        public required IResponse Response { get; init; }
    }
}
