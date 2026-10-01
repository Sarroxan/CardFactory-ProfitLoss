using System.Windows;

namespace CardFactory.ProfitLoss.App;

public partial class App : Application
{
    // Stage 6B.52: before any window can write a diagnostic, move the files earlier builds
    // left on the desktop into the app's own folder.
    protected override void OnStartup(StartupEventArgs e)
    {
        Infrastructure.AppFiles.MoveOffDesktop();
        base.OnStartup(e);
    }
}
