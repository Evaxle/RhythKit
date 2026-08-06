using System.IO;
using System.Windows;

namespace RhythKit;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var defaultDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "colorsets");
        if (!Directory.Exists(defaultDir))
            Directory.CreateDirectory(defaultDir);
    }
}
