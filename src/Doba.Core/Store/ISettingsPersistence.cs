using Doba.Core.Model;

namespace Doba.Core.Store;

/// <summary>Loads and saves <see cref="AppSettings"/>. Implemented by Doba.Storage.</summary>
public interface ISettingsPersistence
{
    AppSettings Load();

    /// <summary>Write-behind; must not block on disk I/O.</summary>
    void Save(AppSettings settings);
}
