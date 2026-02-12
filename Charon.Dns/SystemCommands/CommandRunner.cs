using System.Diagnostics;
using System.Text;
using Serilog;

namespace Charon.Dns.SystemCommands;

public class CommandRunner(
    ILogger logger) : ICommandRunner
{
    public async Task<bool> Execute<T>(
        T command,
        CancellationToken token = default)
        where T : ICommand
    {
        try
        {
            var builder = new StringBuilder();

            command.BuildCommand(builder);
            EscapeQuotes(builder);
            var commandText = builder.ToString();

            logger.Information("Executing command '{Command}'", commandText);

#if DEBUG
            logger.Warning("Execution of command '{Command}' skipped in debug mode", commandText);
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

            logger.Information("Command '{Command}' executed with exit code = {Code}", commandText, process.ExitCode);

            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Error in {ClassName}", nameof(CommandRunner));
            return false;
        }
    }

    private static void EscapeQuotes(StringBuilder commandBuilder)
    {
        commandBuilder.Replace("\"", "\\\"");
    }
}