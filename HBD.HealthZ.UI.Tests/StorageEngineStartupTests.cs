using FluentAssertions;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;

namespace HBD.HealthZ.UI.Tests;

/// <summary>
/// DRK-1133: closes the gap DRK-1131 identified — <see cref="HealthzUiCofigTests"/> only proves
/// each engine <em>resolves</em>, never that the host actually <em>starts</em> with it. Every
/// test here forces <see cref="HealthzUiWebApplicationFactory.Server"/>, which builds the real
/// <c>Program.cs</c> host and runs every <c>IHostedService</c> — including
/// <c>UIInitializationHostedService</c>, the exact path that threw in DRK-1131's report — so a
/// startup failure here throws instead of passing.
///
/// <c>Memory</c> and <c>SqLite</c> need no external server and run for real, in-process.
/// <c>NpgSql</c> and <c>MySql</c> run against a real container (Testcontainers) because both
/// engines execute EF migrations at startup — resolving the engine proves nothing about whether
/// those migrations complete. <c>SqlServer</c> cannot be exercised on this host at all:
/// <c>mcr.microsoft.com/mssql/server</c> publishes no arm64 manifest (verified via
/// <c>docker manifest inspect</c>, single-arch amd64-only), and this test host is aarch64 — see
/// the Skip reason below rather than a silently-passing or silently-missing cell.
///
/// This suite runs on native arm64. It says nothing about the amd64 startup path — that is
/// covered separately, on native amd64 hardware, by the <c>docker-build</c> job in
/// <c>.github/workflows/build-test.yml</c> (DRK-1132).
/// </summary>
public class StorageEngineStartupTests
{
    [Fact]
    public void Memory_NoConnectionStringNeeded_HostStartsSuccessfully()
    {
        using var factory = new HealthzUiWebApplicationFactory();
        factory.ConfigOverrides["HealthChecksUI:DbType"] = "Memory";

        var act = () => { _ = factory.Server; };

        act.Should().NotThrow("in-memory storage must complete startup with no external dependency");
    }

    [Fact]
    public async Task SqLite_RealFileDatabase_HostStartsAndServes()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"healthz-startup-{Guid.NewGuid():N}.db");
        try
        {
            using var factory = new HealthzUiWebApplicationFactory();
            factory.ConfigOverrides["HealthChecksUI:DbType"] = "SqLite";
            factory.ConfigOverrides["ConnectionStrings:DbConn"] = $"Data Source={dbFile}";

            var act = () => { _ = factory.Server; };
            act.Should().NotThrow("SqLite storage must migrate and start against a real file database");

            using var client = factory.CreateClient();
            var response = await client.GetAsync("/");
            ((int)response.StatusCode).Should().BeLessThan(500, "the host must be serving requests, not failed");
        }
        finally
        {
            if (File.Exists(dbFile))
                File.Delete(dbFile);
        }
    }

    [Fact]
    public async Task NpgSql_RealPostgresContainer_HostStartsAndServes()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();

        using var factory = new HealthzUiWebApplicationFactory();
        factory.ConfigOverrides["HealthChecksUI:DbType"] = "NpgSql";
        factory.ConfigOverrides["ConnectionStrings:DbConn"] = postgres.GetConnectionString();

        var act = () => { _ = factory.Server; };
        act.Should().NotThrow("NpgSql storage must complete its startup migration against a real PostgreSQL server");

        using var client = factory.CreateClient();
        var response = await client.GetAsync("/");
        ((int)response.StatusCode).Should().BeLessThan(500, "the host must be serving requests, not failed");
    }

    [Fact]
    public async Task MySql_RealMySqlContainer_HostStartsAndServes()
    {
        await using var mysql = new MySqlBuilder("mysql:8.4").Build();
        await mysql.StartAsync();

        using var factory = new HealthzUiWebApplicationFactory();
        factory.ConfigOverrides["HealthChecksUI:DbType"] = "MySql";
        factory.ConfigOverrides["ConnectionStrings:DbConn"] = mysql.GetConnectionString();

        var act = () => { _ = factory.Server; };
        act.Should().NotThrow("MySql storage must complete its startup migration against a real MySQL server");

        using var client = factory.CreateClient();
        var response = await client.GetAsync("/");
        ((int)response.StatusCode).Should().BeLessThan(500, "the host must be serving requests, not failed");
    }

    [Fact(Skip =
        "SqlServer cannot be exercised on this host: mcr.microsoft.com/mssql/server publishes a " +
        "single-arch amd64 manifest only (confirmed via 'docker manifest inspect' — no manifest " +
        "list, no arm64 entry), and this test host is aarch64. No Testcontainers.MsSql image can " +
        "start here. Not covered by any other test in this repo either — CI's docker-build guard " +
        "(DRK-1132) only exercises the shipped default and DbType=Memory on amd64, not SqlServer. " +
        "This is an honestly-reported gap, not a silently-skipped one.")]
    public void SqlServer_UnreachableOnThisArm64Host_DocumentedGap()
    {
    }
}
