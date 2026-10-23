using Microsoft.UI.Xaml;
using Microsoft.Windows.AppNotifications;

namespace TaskList.App;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
        // Keep a record of anything that slips through, so a crash can be diagnosed afterwards.
        UnhandledException += (_, e) =>
        {
            try
            {
                var log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskList", "crash.log");
                File.AppendAllText(log, $"{DateTime.Now:u} {e.Exception}{Environment.NewLine}{e.Message}{Environment.NewLine}{Environment.NewLine}");
            }
            catch (IOException)
            {
            }
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            // Lets background research announce itself with a Windows notification.
            AppNotificationManager.Default.Register();
        }
        catch (Exception)
        {
        }
        _window = new MainWindow();
        _window.Activate();
    }
}
