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
    
    public static string? GetOptionalSectionValue(this IConfigurationSection section)
    {
        return section.Value;
    }
    
    public static string? GetOptionalSectionValue(this IConfigurationSection section, string key)
    {
        return section.GetSection(key).Value;
    }
    
    public static T GetSectionValue<T>(this IConfigurationSection section) where T : IParsable<T>
    {
        var rawValue = section.Value;
        if (!T.TryParse(rawValue, null, out var value))
        {
            throw new SettingsValidationException($"Unable to parse value for '{section.Path}'");
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
    
    public static T GetSectionEnumValue<T>(this IConfigurationSection section, string key, T defaultValue) where T : struct, Enum
    {
        var rawValue = section.GetSection(key).Value;
        if (Enum.TryParse<T>(rawValue, out var value)
            && Enum.IsDefined(value))
        { 
            return value; 
        }
            
        return defaultValue;
        
    }

    /// <summary>
    /// Unlike <see cref="GetSectionEnumValue{T}"/>, fails on an unknown value instead of falling back to the default.
    /// </summary>
    public static T GetStrictSectionEnumValue<T>(this IConfigurationSection section, string key, T defaultValue) where T : struct, Enum
    {
        var rawValue = section.GetSection(key).Value;
        if (rawValue is null)
        {
            return defaultValue;
        }

        // Only names are accepted: Enum.TryParse also accepts numbers
        var isName = rawValue.Length > 0 && char.IsAsciiLetter(rawValue[0]);
        if (!isName
            || !Enum.TryParse<T>(rawValue, ignoreCase: true, out var value)
            || !Enum.IsDefined(value))
        {
            var allowedValues = string.Join(", ", Enum.GetNames<T>());
            throw new SettingsValidationException(
                $"Invalid value '{rawValue}' for key '{key}' in '{section.Path}'. Allowed values: {allowedValues}");
        }

        return value;
    }
}
