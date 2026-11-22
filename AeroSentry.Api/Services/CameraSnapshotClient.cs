using System.Net.Http.Headers;
using System.Text;
using AeroSentry.Api.Data.Entities;
using AeroSentry.Api.Services.Interfaces;

namespace AeroSentry.Api.Services;

public class CameraSnapshotClient : ICameraSnapshotClient
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;

    public CameraSnapshotClient(HttpClient http, IConfiguration config)
    {
        _http = http;
        _config = config;
    }

    public async Task<byte[]> GetJpegAsync(Camera camera)
    {
        // IP Webcam serves one still picture at /shot.jpg (its live video is at /video).
        var path = _config["Media:MobileSnapshotPath"] ?? "/shot.jpg";
        var uri = new UriBuilder("http", camera.Host, camera.Port, path).Uri;

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (camera.Username is not null)
        {
            // IP Webcam protects itself with HTTP Basic login when a username and password are set.
            var login = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{camera.Username}:{camera.Password}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", login);
        }

        try
        {
            using var response = await _http.SendAsync(request);

            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                throw new CameraSnapshotException("The camera refused the username or password");
            if (!response.IsSuccessStatusCode)
                throw new CameraSnapshotException($"The camera answered {(int)response.StatusCode}");

            return await response.Content.ReadAsByteArrayAsync();
        }
        catch (HttpRequestException)
        {
            throw new CameraSnapshotException("The camera is not reachable");
        }
        catch (TaskCanceledException)
        {
            throw new CameraSnapshotException("The camera did not answer in time");
        }
    }
}
