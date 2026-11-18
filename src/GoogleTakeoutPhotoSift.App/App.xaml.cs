using System.Windows;
using Tiesmaster.GoogleTakeoutPhotoSift.Core.Batches;
using Tiesmaster.GoogleTakeoutPhotoSift.Core.Settings;
using CommandLine = Tiesmaster.GoogleTakeoutPhotoSift.Core.Settings.CommandLine;

namespace Tiesmaster.GoogleTakeoutPhotoSift.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        CommandLine commandLine;
        try
        {
            commandLine = CommandLine.Parse(e.Args);
        }
        catch (FormatException error)
        {
            Fail($"{error.Message}\n\n{CommandLine.Usage}");
            return;
        }

        var settings = SiftSettings.Load();
        var root = commandLine.BatchesRoot ?? settings.BatchesRoot;
        if (BatchLocator.Find(commandLine.Batch, root) is not { } batch)
        {
            Fail($"No batch folder found for '{commandLine.Batch}' (looked in {root} and {root}\\done).");
            return;
        }

        MainWindow = new MainWindow(commandLine, settings, batch);
        MainWindow.Show();
    }

    private void Fail(string message)
    {
        MessageBox.Show(message, "Google Takeout Photo Sift", MessageBoxButton.OK, MessageBoxImage.Error);
        Shutdown(2);
    }
}
