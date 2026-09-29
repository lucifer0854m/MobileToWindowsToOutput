using Microsoft.UI.Xaml;

namespace PhoneSpeaker;

public partial class App : Application
{
    private Window? _window;
    public App() => InitializeComponent();
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var startInBackground = Environment.GetCommandLineArgs()
            .Any(argument => argument.Equals("--background", StringComparison.OrdinalIgnoreCase));
        _window = new MainWindow(startInBackground);
        _window.Activate();
    }
}
