using AeroSentry.Api.Data.Entities;

namespace AeroSentry.Api.Services;

// Fetches one still JPEG from a phone camera running the IP Webcam app (plain HTTP, optional login).
// The admin app draws the wall annotation on this picture.
public interface ICameraSnapshotClient
{
    Task<byte[]> GetJpegAsync(Camera camera);
}

// The camera could not give us a picture (offline, wrong login, ...). The message is safe to show to the admin.
public class CameraSnapshotException : Exception
{
    public CameraSnapshotException(string message) : base(message) { }
}
