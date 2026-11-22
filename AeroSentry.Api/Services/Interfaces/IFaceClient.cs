namespace AeroSentry.Api.Services.Interfaces;

// The only class that talks to the face service (port 8020). It finds faces in a picture and returns their embeddings.
public interface IFaceClient
{
    // detSize: how hard to look for small faces (320 to 1920, default 640; bigger = finds smaller faces but slower).
    Task<FaceAnalysis> AnalyzeAsync(byte[] image, int? detSize = null, CancellationToken cancellationToken = default);

    // Never throws.
    Task<bool> IsAvailableAsync();
}
