using Microsoft.Extensions.Configuration;

namespace Charon.Dns.Settings;

public interface ISettings<out TSettings> where TSettings : class, ISettings<TSettings>
{
    static abstract TSettings Initialize(IConfiguration config);
}