using Umbraco.Cms.Core.Packaging;

namespace BaryoDev.Umbraco.Analytics.Data;

public sealed class AnalyticsMigrationPlan() : PackageMigrationPlan("BaryoDev.Umbraco.Analytics")
{
    protected override void DefinePlan()
    {
        To<CreateTables>("2b8d5e61-7c3a-4f0e-a1d9-5e4c6b7a8f32");
    }
}
