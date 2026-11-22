using AeroSentry.Api.Data;
using AeroSentry.Api.Data.Entities;
using AeroSentry.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AeroSentry.Api.Services;

public class AlertService : IAlertService
{
    private readonly AppDbContext _db;
    private readonly IAlertNotifier _notifier;
    private readonly ILogger<AlertService> _logger;

    public AlertService(AppDbContext db, IAlertNotifier notifier, ILogger<AlertService> logger)
    {
        _db = db;
        _notifier = notifier;
        _logger = logger;
    }

    public async Task<AlertResponse> CreateForDetectionAsync(DetectionEvent detection, Guid propertyId)
    {
        var alert = new Alert
        {
            DetectionEvent = detection,   // links by navigation: both rows are saved together below
            PropertyId = propertyId,
            WallId = detection.WallId,
            CellId = detection.CellId,
            CellLabel = detection.CellLabel,
            Probability = detection.Probability
        };

        _db.Alerts.Add(alert);
        await _db.SaveChangesAsync();   // saves the detection event and the alert in one go

        var response = await LoadResponseAsync(alert.Id);

        // The alert is safely stored. A failed push (nobody listening, hub hiccup) must not fail the frame.
        try
        {
            await _notifier.NotifyAsync(response);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not push alert {Code} over SignalR", response.IncidentCode);
        }

        return response;
    }

    public async Task<Result<PagedResponse<AlertResponse>>> ListAsync(
        Guid userId, string role, Guid? propertyId, string? status, int page, int pageSize)
    {
        if (page < 1 || pageSize < 1 || pageSize > 100)
            return Result<PagedResponse<AlertResponse>>.BadRequest("page must be 1 or more and pageSize from 1 to 100");

        if (status is not null && !AlertStatus.All.Contains(status))
            return Result<PagedResponse<AlertResponse>>.BadRequest($"status must be one of: {string.Join(", ", AlertStatus.All)}");

        if (propertyId is not null && !await _db.CanSeeProperty(userId, role, propertyId.Value))
            return Result<PagedResponse<AlertResponse>>.NotFound("Property not found");

        var visible = _db.VisibleProperties(userId, role).Select(p => p.Id);
        var query = _db.Alerts.Where(a => visible.Contains(a.PropertyId));

        if (propertyId is not null)
            query = query.Where(a => a.PropertyId == propertyId);
        if (status is not null)
            query = query.Where(a => a.Status == status);

        var total = await query.CountAsync();
        var rows = await query
            .Include(a => a.Property)
            .Include(a => a.Wall).ThenInclude(w => w.Camera)
            .OrderByDescending(a => a.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Result<PagedResponse<AlertResponse>>.Ok(new PagedResponse<AlertResponse>
        {
            Items = rows.Select(ToResponse).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = total
        });
    }

    public async Task<Result<AlertResponse>> GetAsync(Guid userId, string role, Guid alertId)
    {
        var visible = _db.VisibleProperties(userId, role).Select(p => p.Id);

        var alert = await _db.Alerts
            .Include(a => a.Property)
            .Include(a => a.Wall).ThenInclude(w => w.Camera)
            .FirstOrDefaultAsync(a => a.Id == alertId && visible.Contains(a.PropertyId));

        return alert is null
            ? Result<AlertResponse>.NotFound("Alert not found")
            : Result<AlertResponse>.Ok(ToResponse(alert));
    }

    // ---- helpers ----

    private async Task<AlertResponse> LoadResponseAsync(Guid alertId)
    {
        var alert = await _db.Alerts
            .Include(a => a.Property)
            .Include(a => a.Wall).ThenInclude(w => w.Camera)
            .FirstAsync(a => a.Id == alertId);

        return ToResponse(alert);
    }

    private static AlertResponse ToResponse(Alert a) => new()
    {
        Id = a.Id,
        Number = a.Number,
        IncidentCode = $"INC-{a.Number:D4}",
        Status = a.Status,
        PropertyId = a.PropertyId,
        PropertyName = a.Property.Name,
        WallId = a.WallId,
        WallName = a.Wall.Name,
        CameraId = a.Wall.CameraId,
        CameraName = a.Wall.Camera.Name,
        CellId = a.CellId,
        CellLabel = a.CellLabel,
        Probability = a.Probability,
        DetectionEventId = a.DetectionEventId,
        CreatedAtUtc = a.CreatedAtUtc
    };
}
