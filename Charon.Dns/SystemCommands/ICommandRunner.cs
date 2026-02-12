namespace Charon.Dns.SystemCommands;

public interface ICommandRunner
{
    Task<bool> Execute<T>(
        T command,
        CancellationToken token = default)
        where T : ICommand;
}