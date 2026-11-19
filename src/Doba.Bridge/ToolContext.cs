using Doba.Core.Model;
using Doba.Core.Store;

namespace Doba.Bridge;

/// <summary>What <see cref="ToolExecutor"/> runs one request against (on Doba's UI thread).</summary>
/// <param name="store">The live task store.</param>
/// <param name="settings">The settings at the time of the request (access level, default priority, week start…).</param>
/// <param name="clock">Doba's clock.</param>
/// <param name="openEditId">Id of the task open in Doba's editor right now, or null.</param>
public sealed class ToolContext(ITaskStore store, AppSettings settings, TimeProvider clock, long? openEditId)
{
    public ITaskStore Store { get; } = store;

    public AppSettings Settings { get; } = settings;

    public TimeProvider Clock { get; } = clock;

    public long? OpenEditId { get; } = openEditId;
}
