using Umbraco.Cms.Infrastructure.Migrations;

namespace BaryoDev.Umbraco.Analytics.Data;

public sealed class CreateTables(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!TableExists(EventRow.TableName))
        {
            Create.Table<EventRow>().Do();
        }
        if (!TableExists(DailyRow.TableName))
        {
            Create.Table<DailyRow>().Do();
        }
        return Task.CompletedTask;
    }
}
