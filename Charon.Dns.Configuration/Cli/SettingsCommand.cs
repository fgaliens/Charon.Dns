using System.CommandLine;
using Charon.Dns.Configuration.Migrations;

namespace Charon.Dns.Configuration.Cli;

/// <summary>
/// "settings" command group for managing configuration files from the command line.
/// </summary>
/// <remarks>
/// Commands that edit the files should load a <see cref="Documents.ConfigWorkspace"/>,
/// migrate it with <see cref="ConfigMigrator.Migrate"/>, edit it and save it.
/// </remarks>
public static class SettingsCommand
{
    public static Command Create(ConfigFileNames fileNames)
    {
        var migrateCommand = CreateMigrateCommand(fileNames);

        return new("settings", "Manage configuration files")
        {
            migrateCommand,
        };
    }

    private static Command CreateMigrateCommand(ConfigFileNames fileNames)
    {
        var directoryOption = new Option<DirectoryInfo>("--dir", "-d")
        {
            Description = "Directory with configuration files (default: current directory)",
            DefaultValueFactory = _ => new DirectoryInfo(Directory.GetCurrentDirectory()),
        };

        var command = new Command("migrate", "Convert configuration files to the format of this version")
        {
            directoryOption,
        };

        command.SetAction(parseResult =>
        {
            var directory = parseResult.GetValue(directoryOption)!;
            return Migrate(directory, fileNames);
        });

        return command;
    }

    private static int Migrate(DirectoryInfo directory, ConfigFileNames fileNames)
    {
        MigrationResult result;
        try
        {
            result = ConfigMigrator.Default.MigrateFiles(directory.FullName, fileNames);
        }
        catch (Exception e) when (e is ConfigException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Unable to migrate configuration files: {e.Message}");
            return 1;
        }

        WriteResult(result);
        return 0;
    }

    private static void WriteResult(MigrationResult result)
    {
        if (!result.IsMigrated)
        {
            Console.WriteLine($"Configuration files are up to date (version {result.ToVersion})");
            return;
        }

        Console.WriteLine($"Configuration files migrated from version {result.FromVersion} to {result.ToVersion}");
        foreach (var note in result.Notes)
        {
            Console.WriteLine($"  - {note}");
        }

        foreach (var backupFile in result.BackupFiles)
        {
            Console.WriteLine($"  Backup: {backupFile}");
        }
    }
}
