using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using Charon.Dns.Extensions;
using Charon.Dns.Settings;
using Serilog;

namespace Charon.Dns.RequestResolving;

public class HostNameAnalyzer : IHostNameAnalyzer
{
    private readonly FrozenDictionary<string, SecuredConnectionParams> _domainMatchedHostnames;
    private readonly ConcurrentDictionary<string, SecuredConnectionParams?> _substringMatchedHostnames = new(StringComparer.OrdinalIgnoreCase);
    private readonly FrozenSet<string> _blockedHostnames;
    private readonly ILogger _logger;

    public HostNameAnalyzer(
        RoutingSettings routingSettings,
        ILogger logger)
    {
        _blockedHostnames = routingSettings.BlockedHostNames.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        
        logger.Information("Found {ItemsCount} host names to block", _blockedHostnames.Count);

        var securedConnectionParamsCache = new Dictionary<RoutingSettingsItem, SecuredConnectionParams>();
        foreach (var routingSettingsItem in routingSettings.Items)
        {
            if (!securedConnectionParamsCache.ContainsKey(routingSettingsItem))
            {
                securedConnectionParamsCache.Add(routingSettingsItem, new SecuredConnectionParams
                {
                    InterfaceName = routingSettingsItem.InterfaceToRouteThrough,
                    IpV4RoutingSubnet = routingSettingsItem.IpV4RoutingSubnet,
                    IpV6RoutingSubnet = routingSettingsItem.IpV6RoutingSubnet,
                });
            }
        }
        
        _domainMatchedHostnames = routingSettings
            .Items
            .TurnOut(x => x.MatchedByDomainHostNames)
            .DistinctBy(x => x.Key, DomainNameComparer.Instance)
            .ToFrozenDictionary(
                x => x.Key, 
                x => securedConnectionParamsCache[x.Value], 
                DomainNameComparer.Instance);
        
        logger.Information("Found {ItemsCount} host names to be secured (by domain name)", _domainMatchedHostnames.Count);

        var substringMatchedHostnames = routingSettings
            .Items
            .TurnOut(x => x.MatchedBySubstringHostNames)
            .Select(x => (x.Key, securedConnectionParamsCache[x.Value]));

        var hostNameSubstringsCount = 0;
        foreach ((string hostNameSubstring, SecuredConnectionParams connectionParams) in substringMatchedHostnames)
        {
            for (int i = 1; i < hostNameSubstring.Length; i++)
            {
                _substringMatchedHostnames.TryAdd(hostNameSubstring[..i], null);
            }
            _substringMatchedHostnames.TryAdd(hostNameSubstring, connectionParams);
            hostNameSubstringsCount++;
        }
        
        logger.Information("Found {ItemsCount} host names to be secured (by domain substring; indexes: {IndexesCount})", 
            hostNameSubstringsCount, _substringMatchedHostnames.Count);
        
        _logger = logger;
    }

    public bool ShouldBeSecured(string domainName)
    {
        var result = ShouldBeSecuredInternal(domainName, out _);
        _logger.Debug("Host name '{Host}' should be secured: {IsSecured}", domainName, result);
        return result;
    }
    
    public bool ShouldBeSecured(string domainName, [NotNullWhen(true)] out SecuredConnectionParams? connectionParams)
    {
        var result = ShouldBeSecuredInternal(domainName, out connectionParams);
        _logger.Debug("Host name '{Host}' should be secured: {IsSecured}", domainName, result);
        return result;
    }

    public bool ShouldBeBlocked(string domainName)
    {
        return _blockedHostnames.Contains(domainName);
    }

    private bool ShouldBeSecuredInternal(string domainName, [NotNullWhen(true)] out SecuredConnectionParams? connectionParams)
    {
        if (_domainMatchedHostnames.TryGetValue(domainName, out connectionParams))
        {
            _logger.Debug("Host name '{Host}' should be secured because it is matched by domain", domainName);
            
            return true;
        }
        
        var substringIndexesLookup = _substringMatchedHostnames.GetAlternateLookup<ReadOnlySpan<char>>();
        for (int i = 0; i < domainName.Length; i++)
        {
            for (int j = i + 1; j < domainName.Length; j++)
            {
                var domainNameSubstring = domainName.AsSpan(i..j);
                if (!substringIndexesLookup.TryGetValue(domainNameSubstring, out var foundedParams))
                {
                    break;
                }

                if (foundedParams is not null)
                {
                    connectionParams = foundedParams;
                    return true;
                }
            }
        }

        connectionParams = null;
        return false;
    }
}

public record SecuredConnectionParams
{
    public required string InterfaceName { get; init; }
    public required byte IpV4RoutingSubnet { get; init; }
    public required byte IpV6RoutingSubnet { get; init; }
}
