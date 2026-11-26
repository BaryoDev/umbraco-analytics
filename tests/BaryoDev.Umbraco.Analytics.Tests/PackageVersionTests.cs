using System.Text.Json;
using System.Xml.Linq;

namespace BaryoDev.Umbraco.Analytics.Tests;

/// <summary>The dashboard script URL carries the package version, so browsers fetch the new script after an upgrade.</summary>
public class PackageVersionTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BaryoDev.Umbraco.Analytics.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [Fact]
    public void Manifest_version_and_script_url_match_the_package_version()
    {
        var src = Path.Combine(Root(), "src", "BaryoDev.Umbraco.Analytics");
        var version = XDocument.Load(Path.Combine(src, "BaryoDev.Umbraco.Analytics.csproj"))
            .Descendants("Version").Single().Value;
        using var manifest = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(src, "wwwroot", "App_Plugins", "BaryoDev.Analytics", "umbraco-package.json")));

        Assert.Equal(version, manifest.RootElement.GetProperty("version").GetString());
        var elements = manifest.RootElement.GetProperty("extensions").EnumerateArray()
            .Where(ext => ext.TryGetProperty("element", out _))
            .Select(ext => ext.GetProperty("element").GetString())
            .ToList();
        Assert.NotEmpty(elements);
        Assert.All(elements, element => Assert.EndsWith($".js?v={version}", element));
    }

    [Fact]
    public void Dashboard_loads_the_stylesheet_with_the_script_version_query()
    {
        var script = File.ReadAllText(Path.Combine(Root(), "src", "BaryoDev.Umbraco.Analytics",
            "wwwroot", "App_Plugins", "BaryoDev.Analytics", "analytics-dashboard.js"));

        Assert.Contains("const V = new URL(import.meta.url).search;", script);
        Assert.Contains("dashboard.css${V}", script);
    }
}
