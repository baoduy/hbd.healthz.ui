using FluentAssertions;
using HBD.HealthZ.UI.Configs;
using Microsoft.AspNetCore.Builder;

namespace HBD.HealthZ.UI.Tests;

/// <summary>
/// Unit tests for the pure storage-engine / history-depth resolution logic in
/// <see cref="HealthzUiCofig"/> — covers the "every advertised storage engine can be selected"
/// and "falling back to non-persistent history is reported" acceptance scenarios.
/// </summary>
public class HealthzUiCofigTests
{
    [Theory]
    [InlineData("SqlServer", DbTypes.SqlServer)]
    [InlineData("NpgSql", DbTypes.NpgSql)]
    [InlineData("MySql", DbTypes.MySql)]
    [InlineData("SqLite", DbTypes.SqLite)]
    [InlineData("sqlserver", DbTypes.SqlServer)] // case-insensitive
    public void ResolveStorageEngine_WithMatchingConnectionString_ResolvesConfiguredEngine(
        string configuredDbType, DbTypes expected)
    {
        var result = HealthzUiCofig.ResolveStorageEngine(configuredDbType, "some-connection-string");

        result.Engine.Should().Be(expected);
        result.FellBackToMemory.Should().BeFalse();
        result.FallbackReason.Should().BeNull();
    }

    [Fact]
    public void ResolveStorageEngine_MemoryConfigured_ResolvesMemoryWithoutFallbackFlag()
    {
        var result = HealthzUiCofig.ResolveStorageEngine("Memory", connectionString: null);

        result.Engine.Should().Be(DbTypes.Memory);
        result.FellBackToMemory.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveStorageEngine_PersistentEngineWithNoConnectionString_FallsBackToMemoryWithReason(
        string? connectionString)
    {
        var result = HealthzUiCofig.ResolveStorageEngine("SqlServer", connectionString);

        result.Engine.Should().Be(DbTypes.Memory);
        result.FellBackToMemory.Should().BeTrue();
        result.FallbackReason.Should().Contain("ConnectionStrings:DbConn is empty");
    }

    [Fact]
    public void ResolveStorageEngine_UnrecognisedEngineName_FallsBackToMemoryWithReason()
    {
        var result = HealthzUiCofig.ResolveStorageEngine("Redis", "some-connection-string");

        result.Engine.Should().Be(DbTypes.Memory);
        result.FellBackToMemory.Should().BeTrue();
        result.FallbackReason.Should().Contain("Redis").And.Contain("not a recognised storage engine");
    }

    [Fact]
    public void ResolveStorageEngine_NumericButUnnamedEnumValue_FallsBackToMemoryWithReason()
    {
        // Enum.TryParse accepts an in-range numeric string even with no matching member (e.g.
        // "3" sits between NpgSql=2 and MySql=4) — this must not crash or silently mis-resolve.
        var result = HealthzUiCofig.ResolveStorageEngine("3", "some-connection-string");

        result.Engine.Should().Be(DbTypes.Memory);
        result.FellBackToMemory.Should().BeTrue();
        result.FallbackReason.Should().Contain("not a recognised storage engine");
    }

    [Fact]
    public void ResolveStorageEngine_NullDbType_FallsBackToMemoryWithReason()
    {
        var result = HealthzUiCofig.ResolveStorageEngine(null, "some-connection-string");

        result.Engine.Should().Be(DbTypes.Memory);
        result.FellBackToMemory.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveMaxHistoryEntriesPerEndpoint_Unset_UsesDefaultWithoutClamping(string? configuredValue)
    {
        var result = HealthzUiCofig.ResolveMaxHistoryEntriesPerEndpoint(configuredValue);

        result.Value.Should().Be(HealthzUiCofig.DefaultMaxHistoryEntriesPerEndpoint);
        result.Clamped.Should().BeFalse();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("not-a-number")]
    public void ResolveMaxHistoryEntriesPerEndpoint_NonPositiveOrUnparseable_ClampsToDefault(string configuredValue)
    {
        var result = HealthzUiCofig.ResolveMaxHistoryEntriesPerEndpoint(configuredValue);

        result.Value.Should().Be(HealthzUiCofig.DefaultMaxHistoryEntriesPerEndpoint);
        result.Clamped.Should().BeTrue();
    }

    [Theory]
    [InlineData("25", 25)]
    [InlineData("1", 1)]
    public void ResolveMaxHistoryEntriesPerEndpoint_PositiveInteger_UsesConfiguredValue(
        string configuredValue, int expected)
    {
        var result = HealthzUiCofig.ResolveMaxHistoryEntriesPerEndpoint(configuredValue);

        result.Value.Should().Be(expected);
        result.Clamped.Should().BeFalse();
    }

    [Theory]
    [InlineData("SqlServer")]
    [InlineData("NpgSql")]
    [InlineData("MySql")]
    [InlineData("SqLite")]
    [InlineData("Memory")]
    public void AddHealthzUiCofig_EveryAdvertisedEngineWithConnectionSupplied_WiresUpWithoutFallback(string dbType)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["HealthChecksUI:DbType"] = dbType;
        builder.Configuration["ConnectionStrings:DbConn"] = "Data Source=test";

        var originalOut = Console.Out;
        var captured = new StringWriter();
        Console.SetOut(captured);
        try
        {
            builder.AddHealthzUiCofig();
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        // No engine falls back to in-memory when a matching connection is supplied.
        captured.ToString().Should().NotContain("Health history will not survive a restart");
    }

    [Fact]
    public void AddHealthzUiCofig_NoConnectionStringSupplied_FallsBackAndLogsWarning()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["HealthChecksUI:DbType"] = "SqlServer";
        // Deliberately no ConnectionStrings:DbConn.

        var originalOut = Console.Out;
        var captured = new StringWriter();
        Console.SetOut(captured);
        try
        {
            builder.AddHealthzUiCofig();
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var output = captured.ToString();
        output.Should().Contain("warn", "the fallback must be logged at a level an operator will see")
            .And.Contain("ConnectionStrings:DbConn is empty")
            .And.Contain("Health history will not survive a restart");
    }

    [Fact]
    public void AddHealthzUiCofig_NonPositiveMaxHistoryEntries_LogsClampWarning()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["HealthChecksUI:MaximumExecutionHistoriesPerEndpoint"] = "0";
        builder.Configuration["HealthChecksUI:DbType"] = "Memory";

        var originalOut = Console.Out;
        var captured = new StringWriter();
        Console.SetOut(captured);
        try
        {
            builder.AddHealthzUiCofig();
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        captured.ToString().Should().Contain("is not a positive integer");
    }
}
