using BaryoDev.Umbraco.Analytics.Collect;

namespace BaryoDev.Umbraco.Analytics.Tests;

public class EventWriterTests
{
    [Fact]
    public void Visitor_map_is_cut_back_to_the_cap_even_when_nobody_is_idle()
    {
        var now = new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);
        var recent = new Dictionary<string, (string VisitId, DateTime Last)>();
        for (int i = 0; i < 50_000; i++)
        {
            recent["v" + i] = ("s", now.AddSeconds(i));
        }

        EventWriter.Prune(recent, cutoff: now.AddDays(-1), EventWriter.MaxRecent);

        Assert.Equal(EventWriter.MaxRecent, recent.Count);
        Assert.True(recent.ContainsKey("v49999"));
        Assert.False(recent.ContainsKey("v0"));
    }

    [Fact]
    public void Idle_visitors_go_first()
    {
        var now = new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);
        var recent = new Dictionary<string, (string VisitId, DateTime Last)>();
        for (int i = 0; i < 30; i++)
        {
            recent["old" + i] = ("s", now.AddHours(-2));
        }
        for (int i = 0; i < 5; i++)
        {
            recent["new" + i] = ("s", now);
        }

        EventWriter.Prune(recent, cutoff: now.AddMinutes(-30), max: 10);

        Assert.Equal(5, recent.Count);
    }
}
