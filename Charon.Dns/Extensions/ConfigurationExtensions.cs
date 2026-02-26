using Charon.Dns.Exceptions;
using Microsoft.Extensions.Configuration;

namespace Charon.Dns.Extensions;

public static class ConfigurationExtensions
{
    public static string GetSectionValue(this IConfigurationSection section)
    {
        var value = section.Value;
        if (value is null)
        {
            throw new SettingsValidationException($"Value for '{section.Path}' is missing");
        }

        return value;
    }
    
    public static string GetSectionValue(this IConfigurationSection section, string key)
    {
        var value = section.GetSection(key).Value;
        if (value is null)
        {
            throw new SettingsValidationException($"Value for key '{key}' in '{section.Path}' is missing");
        }

        return value;
    }
    
    public static T GetSectionValue<T>(this IConfigurationSection section, string key) where T : IParsable<T>
    {
        var rawValue = section.GetSection(key).Value;
        if (!T.TryParse(rawValue, null, out var value))
        {
            throw new SettingsValidationException($"Unable to parse value for key '{key}' in '{section.Path}'");
        }
        
        return value;
    }
    
    public static T GetSectionValue<T>(this IConfigurationSection section, string key, T defaultValue) where T : IParsable<T>
    {
        var rawValue = section.GetSection(key).Value;
        if (!T.TryParse(rawValue, null, out var value))
        {
            return defaultValue;
        }
        
        return value;
    }
}
