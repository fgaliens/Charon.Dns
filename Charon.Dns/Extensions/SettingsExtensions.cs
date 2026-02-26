using Charon.Dns.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Charon.Dns.Extensions;

public static class SettingsExtensions
{
    public static IServiceCollection AddSettings<TSettings>(this IServiceCollection services) 
        where TSettings : class, ISettings<TSettings>
    {
        return services.AddSingleton(serviceProvider =>
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            return TSettings.Initialize(configuration);
        });
    }
    
    public static IEnumerable<string> TryResolveDataFromFiles(this IEnumerable<string> itemsToCheck)
    {
        const string filePrefix = "file:";
        
        foreach (var itemToCheck in itemsToCheck)
        {
            if (!itemToCheck.StartsWith(filePrefix, StringComparison.OrdinalIgnoreCase))
            {
                yield return itemToCheck;
            }
            else
            {
                var path = itemToCheck
                    .Replace(filePrefix, string.Empty, StringComparison.OrdinalIgnoreCase)
                    .Trim();

                if (File.Exists(path))
                {
                    using var reader = File.OpenText(path);
                    while (reader.ReadLine() is { } lineOfText)
                    {
                        var trimmedLineOfText = lineOfText.Trim();
                        if (!trimmedLineOfText.StartsWith('#'))
                        {
                            yield return trimmedLineOfText;
                        }
                    }
                }
            }
        }
    }
}