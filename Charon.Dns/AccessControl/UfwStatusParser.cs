using System.Net;
using Charon.Dns.Extensions;

namespace Charon.Dns.AccessControl;

public static class UfwStatusParser
{
    public static async Task<bool> IsActive(IAsyncEnumerable<string> ufwStatusLines)
    {
        await foreach (var line in ufwStatusLines)
        {
            if (line.Contains("Status: active", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static IAsyncEnumerable<IPNetwork> ExtractDeniedNetworks(
        IAsyncEnumerable<string> ufwStatusLines,
        string tag) =>
        ExtractTaggedNetworks(ufwStatusLines, tag, "DENY");

    public static IAsyncEnumerable<IPNetwork> ExtractAllowedNetworks(
        IAsyncEnumerable<string> ufwStatusLines,
        string tag) =>
        ExtractTaggedNetworks(ufwStatusLines, tag, "ALLOW");

    private static async IAsyncEnumerable<IPNetwork> ExtractTaggedNetworks(
        IAsyncEnumerable<string> ufwStatusLines,
        string tag,
        string action)
    {
        await foreach (var line in ufwStatusLines)
        {
            if (!line.Contains(tag, StringComparison.Ordinal) ||
                !line.Contains(action, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var token in line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (TryParseNetwork(token, out var network))
                {
                    yield return network;
                }
            }
        }
    }

    private static bool TryParseNetwork(string token, out IPNetwork network)
    {
        if (IPNetwork.TryParse(token, out network))
        {
            return true;
        }

        if (IPAddress.TryParse(token, out var address))
        {
            network = address.ToIPNetwork();
            return true;
        }

        return false;
    }
}
