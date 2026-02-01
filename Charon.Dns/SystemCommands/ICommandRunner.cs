namespace Charon.Dns.SystemCommands;

public interface ICommandRunner
{
    ValueTask<bool> Execute<T>(
        T command,
        CancellationToken token = default)
        where T : ICommand;
}