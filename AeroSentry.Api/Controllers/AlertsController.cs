using AeroSentry.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AeroSentry.Api.Controllers;

[Route("api/v1/alerts")]
public class AlertsController : ApiControllerBase
{
    private readonly IAlertService _alerts;

    public AlertsController(IAlertService alerts)
    {
        _alerts = alerts;
    }

    // GET /api/v1/alerts/list[?propertyId=...][&status=New][&page=1&pageSize=20]
    // Newest first. Without propertyId: every property the caller can see (admin: own, guard: assigned).
    [HttpGet("list")]
    public async Task<IActionResult> List(Guid? propertyId, string? status, int page = 1, int pageSize = 20) =>
        FromResult(await _alerts.ListAsync(CallerId, CallerRole, propertyId, status, page, pageSize));

    // GET /api/v1/alerts/get?alertId=...
    [HttpGet("get")]
    public async Task<IActionResult> Get(Guid alertId) =>
        FromResult(await _alerts.GetAsync(CallerId, CallerRole, alertId));
}
