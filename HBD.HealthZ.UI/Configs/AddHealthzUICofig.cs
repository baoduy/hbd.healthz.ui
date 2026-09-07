namespace HBD.HealthZ.UI.Configs;

/// <summary>
/// Outcome of deciding which HealthChecksUI storage engine to use: the engine that will
/// actually be wired up, and — when that differs from what was configured — why.
/// </summary>
public readonly record struct StorageEngineResolution(DbTypes Engine, bool FellBackToMemory, string? FallbackReason);

/// <summary>
/// Outcome of deciding the effective MaximumHistoryEntriesPerEndpoint: the value that will
/// actually be applied, and whether a non-positive/unparseable configured value was clamped
/// back to the default.
/// </summary>
public readonly record struct MaxHistoryEntriesResolution(int Value, bool Clamped);

public static class HealthzUiCofig
{
    public const int DefaultMaxHistoryEntriesPerEndpoint = 10;

    /// <summary>
    /// Pure decision, no I/O: given the configured engine name and connection string, which
    /// storage provider actually gets used. An empty connection string, and an unrecognised
    /// <paramref name="configuredDbType"/>, both fall back to in-memory rather than crash.
    /// Public so dev-qc can unit-test every branch without touching the csproj (no
    /// InternalsVisibleTo available in this sub-task).
    /// </summary>
    public static StorageEngineResolution ResolveStorageEngine(string? configuredDbType, string? connectionString)
    {
        if (!Enum.TryParse<DbTypes>(configuredDbType, ignoreCase: true, out var dbType))
            return new StorageEngineResolution(DbTypes.Memory, true,
                $"HealthChecksUI:DbType '{configuredDbType}' is not a recognised storage engine.");

        if (string.IsNullOrWhiteSpace(connectionString))
            return dbType == DbTypes.Memory
                ? new StorageEngineResolution(DbTypes.Memory, false, null)
                : new StorageEngineResolution(DbTypes.Memory, true,
                    $"HealthChecksUI:DbType is '{dbType}' but ConnectionStrings:DbConn is empty.");

        return dbType switch
        {
            DbTypes.SqlServer or DbTypes.NpgSql or DbTypes.MySql or DbTypes.SqLite or DbTypes.Memory
                => new StorageEngineResolution(dbType, false, null),
            // Enum.TryParse accepts an in-range numeric string (e.g. "3") even with no
            // matching member — catch that here rather than crash on an unnamed value.
            _ => new StorageEngineResolution(DbTypes.Memory, true,
                $"HealthChecksUI:DbType '{dbType}' is not a recognised storage engine.")
        };
    }

    /// <summary>
    /// Pure decision, no I/O: given the raw configured value for
    /// HealthChecksUI:MaximumExecutionHistoriesPerEndpoint, which history depth actually gets
    /// applied. Unset, unparseable, or non-positive values all clamp to
    /// <see cref="DefaultMaxHistoryEntriesPerEndpoint"/> rather than crash at startup.
    /// </summary>
    public static MaxHistoryEntriesResolution ResolveMaxHistoryEntriesPerEndpoint(string? configuredValue)
    {
        if (string.IsNullOrWhiteSpace(configuredValue))
            return new MaxHistoryEntriesResolution(DefaultMaxHistoryEntriesPerEndpoint, false);

        if (!int.TryParse(configuredValue, out var parsed) || parsed <= 0)
            return new MaxHistoryEntriesResolution(DefaultMaxHistoryEntriesPerEndpoint, true);

        return new MaxHistoryEntriesResolution(parsed, false);
    }

    public static WebApplicationBuilder AddHealthzUiCofig(this WebApplicationBuilder builder)
    {
        var configuredMaxHistoryEntries = builder.Configuration["HealthChecksUI:MaximumExecutionHistoriesPerEndpoint"];
        var maxHistoryEntries = ResolveMaxHistoryEntriesPerEndpoint(configuredMaxHistoryEntries);

        var b = builder.Services.AddHealthChecksUI(setup => setup.MaximumHistoryEntriesPerEndpoint(maxHistoryEntries.Value));

        if (maxHistoryEntries.Clamped)
        {
            using var loggerFactory = LoggerFactory.Create(lb => lb.AddConsole());
            loggerFactory.CreateLogger("HealthzUiCofig")
                .LogWarning(
                    "HealthChecksUI:MaximumExecutionHistoriesPerEndpoint '{ConfiguredValue}' is not a positive integer; using default {Default}.",
                    configuredMaxHistoryEntries, DefaultMaxHistoryEntriesPerEndpoint);
        }

        var dbType = builder.Configuration["HealthChecksUI:DbType"];
        var conn = builder.Configuration.GetConnectionString("DbConn");

        var resolution = ResolveStorageEngine(dbType, conn);

        switch (resolution.Engine)
        {
            case DbTypes.SqlServer:
                b.AddSqlServerStorage(conn!);
                break;
            case DbTypes.NpgSql:
                b.AddPostgreSqlStorage(conn!);
                break;
            case DbTypes.MySql:
                b.AddMySqlStorage(conn!);
                break;
            case DbTypes.SqLite:
                b.AddSqliteStorage(conn!);
                break;
            default:
                b.AddInMemoryStorage();
                break;
        }

        if (resolution.FellBackToMemory)
        {
            // Startup warning, before the host (and its configured logging providers) exists —
            // a throwaway bootstrap logger is the only way to surface this at Warning level.
            using var loggerFactory = LoggerFactory.Create(lb => lb.AddConsole());
            loggerFactory.CreateLogger("HealthzUiCofig")
                .LogWarning("{Reason} Health history will not survive a restart.", resolution.FallbackReason);
        }

        return builder;
    }
}
