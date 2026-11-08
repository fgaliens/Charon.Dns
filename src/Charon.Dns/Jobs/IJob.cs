namespace Charon.Dns.Jobs;

public interface IJob
{
    TimeSpan Period { get; }
    Task Execute();
}
