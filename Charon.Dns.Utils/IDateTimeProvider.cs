namespace Charon.Dns.Utils;

public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }
}