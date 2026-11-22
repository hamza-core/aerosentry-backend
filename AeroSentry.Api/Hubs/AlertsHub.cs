using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace AeroSentry.Api.Hubs;

// The real-time channel at /hubs/alerts. Clients only LISTEN: the server pushes an "AlertRaised"
// message (an AlertResponse) to the owner and to the active guards assigned to the property.
// A client connects with its normal JWT: header, or ?access_token=... for browsers (WebSockets cannot set headers).
[Authorize]
public class AlertsHub : Hub
{
}

// SignalR needs to know which user a connection belongs to, so it can send "to user X".
// Our tokens carry the user id in the "sub" claim, so that is what we use.
public class SubUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) => connection.User?.FindFirstValue("sub");
}
