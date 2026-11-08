namespace Charon.Dns.SystemCommands;

public interface ICommandRunner
{
    Task<bool> Execute<T>(T command,
        CancellationToken token = default)
        where T : ICommand;

    Task<bool> ExecuteBatch<T>(
        IEnumerable<T> commands,
        CancellationToken token = default)
        where T : ICommand;

    IAsyncEnumerable<string> ExecuteAndQuery<T>(
        T command,
        CancellationToken token = default)
        where T : ICommand;
}