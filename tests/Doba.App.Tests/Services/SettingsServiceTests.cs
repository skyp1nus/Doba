using Doba.App.Services;
using Doba.Core.Model;
using Doba.Core.Store;

namespace Doba.App.Tests.Services;

public class SettingsServiceTests
{
    private sealed class Recorder : ISettingsPersistence
    {
        public List<AppSettings> Saved { get; } = [];
        public AppSettings Load() => AppSettings.Default;
        public void Save(AppSettings settings) => Saved.Add(settings);
    }

    [Fact]
    public void Update_applies_persists_and_notifies_only_on_change()
    {
        var rec = new Recorder();
        var svc = new SettingsService(AppSettings.Default, rec);
        var events = 0;
        svc.Changed += (_, _) => events++;

        svc.Update(s => s with { Accent = Accent.Sage });
        svc.Update(s => s with { Accent = Accent.Sage });

        Assert.Equal(Accent.Sage, svc.Current.Accent);
        Assert.Single(rec.Saved);
        Assert.Equal(1, events);
    }
}
