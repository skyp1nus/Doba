using Doba.Core.Model;
using Doba.Core.Store;

namespace Doba.App.Services;

/// <summary>Live settings: every change is applied immediately and persisted write-behind.</summary>
public sealed class SettingsService(AppSettings initial, ISettingsPersistence? persistence) : ISettingsService
{
    public AppSettings Current { get; private set; } = initial;

    public event EventHandler<AppSettings>? Changed;

    public void Update(Func<AppSettings, AppSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var next = change(Current);
        if (next == Current)
        {
            return;
        }

        Current = next;
        persistence?.Save(next);
        Changed?.Invoke(this, next);
    }
}
