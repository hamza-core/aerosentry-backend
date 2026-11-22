using System.Text.Json.Serialization;

namespace AeroSentry.Api.Services;

// The only class that talks to the face service (port 8020). It finds faces in a picture and returns their embeddings.
public interface IFaceClient
{
    // detSize: how hard to look for small faces (320 to 1920, default 640; bigger = finds smaller faces but slower).
    Task<FaceAnalysis> AnalyzeAsync(byte[] image, int? detSize = null, CancellationToken cancellationToken = default);

    // Never throws.
    Task<bool> IsAvailableAsync();
}

public class FaceException : Exception
{
    public FaceException(string message, Exception? inner = null) : base(message, inner) { }
}

// The face service is not running, did not answer in time, or its model is still loading (-> 503).
public class FaceUnavailableException : FaceException
{
    public FaceUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
}

// The face service could not read the upload as a picture (-> 400).
public class FaceRejectedException : FaceException
{
    public FaceRejectedException(string message) : base(message) { }
}

// ---- the parts of the face service's JSON that we use (names match it exactly) ----
public class FaceAnalysis
{
    [JsonPropertyName("image_width")] public int ImageWidth { get; set; }
    [JsonPropertyName("image_height")] public int ImageHeight { get; set; }
    // Biggest face first.
    [JsonPropertyName("faces")] public List<FaceInfo> Faces { get; set; } = new();
}

public class FaceInfo
{
    // [x1, y1, x2, y2] in pixels of the picture
    [JsonPropertyName("box")] public int[] Box { get; set; } = Array.Empty<int>();
    [JsonPropertyName("det_score")] public double DetScore { get; set; }
    // 512 numbers, length 1
    [JsonPropertyName("embedding")] public float[] Embedding { get; set; } = Array.Empty<float>();

    public int Width => Box[2] - Box[0];
}
