using Doba.Core.Model;

namespace Doba.App.Services;

/// <summary>Holds the live settings; persisted by Doba.Storage. Changes apply immediately (no Save button).</summary>
public interface ISettingsService
{
    AppSettings Current { get; }

    event EventHandler<AppSettings>? Changed;

    void Update(Func<AppSettings, AppSettings> change);
}
