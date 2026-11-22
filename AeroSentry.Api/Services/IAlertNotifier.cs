using AeroSentry.Api.DTOs;

namespace AeroSentry.Api.Services;

// Pushes a new alert to everyone who may see it, over SignalR.
public interface IAlertNotifier
{
    Task NotifyAsync(AlertResponse alert);
}
