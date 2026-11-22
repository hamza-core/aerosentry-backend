using AeroSentry.Api.Data.Entities;
using AeroSentry.Api.DTOs;

namespace AeroSentry.Api.Services;

public interface IAlertService
{
    // Called by the detection bridge for every stored alarm. `detection` is already added to the
    // database context but not saved yet: the event and its alert are saved together, then pushed live.
    Task<AlertResponse> CreateForDetectionAsync(DetectionEvent detection, Guid propertyId, RecognizedPersonDto? recognized = null);

    // The alerts log. propertyId is optional: leave it out to get every property the caller can see.
    Task<Result<PagedResponse<AlertResponse>>> ListAsync(Guid userId, string role, Guid? propertyId, string? status, int page, int pageSize);

    Task<Result<AlertResponse>> GetAsync(Guid userId, string role, Guid alertId);

    // Acknowledge / mark false alarm / close / escalate (action = one of AlertTransitions' names).
    // 409 when the alert's current status does not allow it. Pushes "AlertUpdated" to the apps.
    Task<Result<AlertResponse>> ChangeStatusAsync(Guid userId, string role, Guid alertId, string action, string? note);

    // The alert's history: who did what and when, oldest first.
    Task<Result<List<AlertActionResponse>>> ListActionsAsync(Guid userId, string role, Guid alertId);
}
