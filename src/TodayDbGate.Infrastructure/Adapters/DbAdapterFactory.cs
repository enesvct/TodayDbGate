using TodayDbGate.Application.Abstractions;
using TodayDbGate.Domain.Enums;

namespace TodayDbGate.Infrastructure.Adapters;

public sealed class DbAdapterFactory(IEnumerable<IDbAdapter> adapters) : IDbAdapterFactory
{
    private readonly Dictionary<DatabaseType, IDbAdapter> _adapters = adapters.ToDictionary(a => a.DatabaseType);

    public IDbAdapter Get(DatabaseType databaseType) =>
        _adapters.TryGetValue(databaseType, out var adapter)
            ? adapter
            : throw new NotSupportedException($"No adapter registered for {databaseType}.");
}
