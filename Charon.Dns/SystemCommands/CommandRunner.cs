using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Charon.Dns.Extensions;
using Charon.Dns.Lib.Tracing;
using Serilog;

namespace Charon.Dns.SystemCommands;

public class CommandRunner(ILogger globalLogger) : ICommandRunner
{
    public async Task<bool> Execute<T>(
        T command,
        RequestTrace trace,
        CancellationToken token = default)
        where T : ICommand
    {
        var logger = trace.GetLogger(globalLogger);
        try
        {
            var builder = new StringBuilder();

            command.BuildCommand(builder);
            EscapeQuotes(builder);
            var commandText = builder.ToString();

            logger.Information("Executing command '{Command}'", commandText);

#if DEBUG
            logger.Warning("Execution of command '{Command}' skipped in debug mode", commandText);
            await Task.Delay(10, token);
            return true;
#endif

            var processStartInfo = new ProcessStartInfo("/bin/bash", $"-c \"{commandText}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process();
            process.StartInfo = processStartInfo;
            process.Start();
            await process.WaitForExitAsync(token);

            logger.Information("Command '{Command}' executed with exit code = {Code}", commandText, process.ExitCode);

            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Error in {ClassName}", nameof(CommandRunner));
            return false;
        }
    }

    public async Task<bool> ExecuteBatch<T>(IEnumerable<T> commands, CancellationToken token = default) where T : ICommand
    {
        try
        {
            var builder = new StringBuilder();

            foreach (var command in commands)
            {
                command.BuildCommand(builder);
                builder.Append(';');
            }
            
            EscapeQuotes(builder);
            var commandText = builder.ToString();

            globalLogger.Information("Executing command '{Command}'", commandText);

#if DEBUG
            globalLogger.Warning("Execution of command '{Command}' skipped in debug mode", commandText);
            await Task.Delay(10, token);
            return true;
#endif

            var procStartInfo = new ProcessStartInfo("/bin/bash", $"-c \"{commandText}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process();
            process.StartInfo = procStartInfo;
            process.Start();
            await process.WaitForExitAsync(token);

            globalLogger.Information("Command '{Command}' executed with exit code = {Code}", commandText, process.ExitCode);

            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            globalLogger.Warning(ex, "Error in {ClassName}", nameof(CommandRunner));
            return false;
        }
    }

    public async IAsyncEnumerable<string> ExecuteAndQuery<T>(
        T command,
        RequestTrace trace,
        [EnumeratorCancellation] CancellationToken token = default)
        where T : ICommand
    {
        var logger = trace.GetLogger(globalLogger);
        var builder = new StringBuilder();

        command.BuildCommand(builder);
        EscapeQuotes(builder);
        var commandText = builder.ToString();

        logger.Information("Executing command '{Command}' and querying its output", commandText);

#if DEBUG
        logger.Warning("Querying of command '{Command}' skipped in debug mode", commandText);
        await Task.Delay(10, token);
        yield break;
#endif

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("/bin/bash", $"-c \"{commandText}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            },
        };

        process.Start();

        try
        {
            string? line;
            while ((line = await process.StandardOutput.ReadLineAsync(token)) != null)
            {
                yield return line;
            }
        }
        finally
        {
            await process.WaitForExitAsync(token);
            logger.Information("Command '{Command}' executed with exit code = {Code}", commandText, process.ExitCode);
        }
    }

    private static void EscapeQuotes(StringBuilder commandBuilder)
    {
        commandBuilder.Replace("\"", "\\\"");
    }
}