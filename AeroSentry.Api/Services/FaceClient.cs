using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AeroSentry.Api.Services.Interfaces;

namespace AeroSentry.Api.Services;

public class FaceClient : IFaceClient
{
    private readonly HttpClient _http;

    public FaceClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<FaceAnalysis> AnalyzeAsync(byte[] image, int? detSize = null, CancellationToken cancellationToken = default)
    {
        var picture = new ByteArrayContent(image);
        picture.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var form = new MultipartFormDataContent { { picture, "image", "picture" } };
        if (detSize is not null)
            form.Add(new StringContent(detSize.Value.ToString(CultureInfo.InvariantCulture)), "det_size");

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsync("api/faces/analyze", form, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new FaceUnavailableException("The face service is not reachable", ex);
        }
        catch (TaskCanceledException ex)
        {
            throw new FaceUnavailableException("The face service did not answer in time", ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
                return JsonSerializer.Deserialize<FaceAnalysis>(body)!;

            if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
                throw new FaceRejectedException(ReadDetail(body));

            throw new FaceUnavailableException($"The face service answered {(int)response.StatusCode}");
        }
    }

    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            using var response = await _http.GetAsync("api/health");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    // FastAPI errors look like {"detail": "..."}.
    private static string ReadDetail(string body)
    {
        try
        {
            return JsonDocument.Parse(body).RootElement.GetProperty("detail").GetString() ?? "The face service rejected the picture";
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return "The face service rejected the picture";
        }
    }
}
