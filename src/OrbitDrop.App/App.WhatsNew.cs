using System.Windows.Threading;
using OrbitDrop.Infrastructure;
using OrbitDrop.UI.Settings;
using OrbitDrop.Windows.Shell;

namespace OrbitDrop.App;

/// <summary>"New in OrbitDrop 0.7", once, a few seconds after the first start on 0.7 (not on a brand-new install's first run).</summary>
public partial class App
{
    private const string WhatsNewVersion = "0.7";

    private void ShowWhatsNewSoon()
    {
        if (AppPackage.IsPackaged || _settings.WhatsNewSeen == WhatsNewVersion || !_settings.OnboardingCompleted) return;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (_settings.WhatsNewSeen == WhatsNewVersion) return;
            Update(s => s.WhatsNewSeen = WhatsNewVersion);
            Log.Info("What's new in 0.7");
            ShowInFront(new WhatsNewWindow(() => OpenSettings("Shortcuts"), () => UpdateManager.OpenUrl(StoreSwitch.StorePage.Replace("cid=app_switch", "cid=app_whatsnew07"))));
        };
        timer.Start();
    }
}
