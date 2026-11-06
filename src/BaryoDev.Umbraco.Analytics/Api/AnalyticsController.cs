using Asp.Versioning;
using BaryoDev.Umbraco.Analytics.Stats;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Management.Controllers;
using Umbraco.Cms.Api.Management.Routing;
using Umbraco.Cms.Web.Common.Authorization;

namespace BaryoDev.Umbraco.Analytics.Api;

/// <summary>Numbers for the backoffice dashboard. Content section access, checked on the server.</summary>
[ApiVersion("1.0")]
[VersionedApiBackOfficeRoute("baryo-analytics")]
[ApiExplorerSettings(GroupName = "Baryo Analytics")]
[Authorize(Policy = AuthorizationPolicies.SectionAccessContent)]
public sealed class AnalyticsController(StatsService stats) : ManagementApiControllerBase
{
    private static readonly int[] Ranges = [7, 30, 90];

    [HttpGet("summary")]
    [ProducesResponseType<Summary>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult GetSummary(int days = 30)
    {
        if (!Ranges.Contains(days))
        {
            return BadRequest(new ProblemDetails { Title = "Bad range", Detail = "days must be 7, 30 or 90.", Status = 400 });
        }
        return Ok(stats.Get(days));
    }
}
