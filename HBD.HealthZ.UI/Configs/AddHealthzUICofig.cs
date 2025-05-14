namespace HBD.HealthZ.UI.Configs;

internal static class HealthzUiCofig
{
    public static WebApplicationBuilder AddHealthzUiCofig(this WebApplicationBuilder builder)
    {
        var b = builder.Services.AddHealthChecksUI();
        var dbType = builder.Configuration.GetValue<DbTypes>("HealthChecksUI:DbType");
        var conn = builder.Configuration.GetConnectionString("DbConn");

        if (!string.IsNullOrWhiteSpace(conn))
        {
            switch (dbType)
            {
                case DbTypes.SqlServer:
                    b.AddSqlServerStorage(conn);
                    break;
                case DbTypes.NpgSql:
                    b.AddPostgreSqlStorage(conn);
                    break;
                case DbTypes.MySql:
                    b.AddMySqlStorage(conn);
                    break;
                case DbTypes.SqLite:
                    b.AddSqliteStorage(conn);
                    break;
                case DbTypes.Memory:
                default:
                    b.AddInMemoryStorage();
                    break;
            }
        }
        else b.AddInMemoryStorage();

        return builder;
    }
}