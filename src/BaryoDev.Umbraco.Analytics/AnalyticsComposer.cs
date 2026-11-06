using BaryoDev.Umbraco.Analytics.Collect;
using BaryoDev.Umbraco.Analytics.Retention;
using BaryoDev.Umbraco.Analytics.Stats;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace BaryoDev.Umbraco.Analytics;

public sealed class AnalyticsComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddOptions<AnalyticsOptions>().BindConfiguration(AnalyticsOptions.Section);
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<SecretProvider>();
        builder.Services.AddSingleton<Collector>();
        builder.Services.AddSingleton<EventQueue>();
        builder.Services.AddSingleton<ContentKeyResolver>();
        builder.Services.AddSingleton<StatsService>();
        builder.Services.AddTransient<IStartupFilter, CollectStartupFilter>();
        builder.Services.AddHostedService<EventWriter>();
        builder.Services.AddHostedService<RetentionJob>();
    }
}
