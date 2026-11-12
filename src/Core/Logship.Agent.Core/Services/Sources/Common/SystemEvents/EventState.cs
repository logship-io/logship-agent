using Logship.Agent.Core.Records;

namespace Logship.Agent.Core.Services.Sources.Common.SystemEvents;

internal sealed record SystemEntity(string Key, string AddedAction, string RemovedAction, Dictionary<string, object> Fields, string Version = "")
{
    internal DataRecord Record(string schema, bool added, string method, DateTimeOffset timestamp)
    {
        var fields = new Dictionary<string, object>(Fields)
        {
            ["machine"] = Environment.MachineName, ["Action"] = added ? AddedAction : RemovedAction,
            ["DetectionMethod"] = method,
        };
        return new(schema, timestamp, fields);
    }
}

internal sealed record SystemChange(SystemEntity? Entity, bool Added, DateTimeOffset Timestamp, Exception? Failure = null)
{
    internal static SystemChange Signal() => new(null, false, DateTimeOffset.UtcNow);
}

internal interface ISystemEventProvider
{
    IReadOnlyList<SystemEntity> Snapshot();
    IDisposable Subscribe(Action<SystemChange> notify);
}

internal sealed class EventState(string schema)
{
    private Dictionary<string, SystemEntity> state = new(StringComparer.Ordinal);
    private bool initialized;

    internal IReadOnlyList<DataRecord> Reconcile(IReadOnlyList<SystemEntity> snapshot,
        IReadOnlyList<SystemChange> pending, string method, DateTimeOffset timestamp)
    {
        var next = snapshot.ToDictionary(e => e.Key, StringComparer.Ordinal);
        var records = new List<DataRecord>();
        if (!initialized)
        {
            state = next;
            initialized = true;
            foreach (var change in pending) Apply(change, records);
            return records;
        }
        foreach (var change in pending)
        {
            Apply(change, records);
            if (change.Entity == null) continue;
            if (state.TryGetValue(change.Entity.Key, out var entity)) next[entity.Key] = entity;
            else next.Remove(change.Entity.Key);
        }
        foreach (var entity in state.Values.Where(e => !next.TryGetValue(e.Key, out var current) || e.Version != current.Version))
            records.Add(entity.Record(schema, false, method, timestamp));
        foreach (var entity in next.Values.Where(e => !state.TryGetValue(e.Key, out var previous) || e.Version != previous.Version))
            records.Add(entity.Record(schema, true, method, timestamp));
        state = next;
        return records;
    }

    internal IReadOnlyList<DataRecord> Apply(SystemChange change)
    {
        var records = new List<DataRecord>();
        Apply(change, records);
        return records;
    }

    private void Apply(SystemChange change, List<DataRecord> records)
    {
        if (change.Entity == null) return;
        var entity = change.Entity;
        if (change.Added)
        {
            if (state.TryGetValue(entity.Key, out var old))
            {
                if (old.Version != entity.Version)
                {
                    records.Add(old.Record(schema, false, "Native", change.Timestamp));
                    records.Add(entity.Record(schema, true, "Native", change.Timestamp));
                    state[entity.Key] = entity;
                    return;
                }
                state[entity.Key] = entity with { Fields = old.Fields.Concat(entity.Fields).GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Last().Value) };
                return;
            }
            state[entity.Key] = entity;
        }
        else if (!state.Remove(entity.Key, out entity)) return;
        records.Add(entity.Record(schema, change.Added, "Native", change.Timestamp));
    }
}
