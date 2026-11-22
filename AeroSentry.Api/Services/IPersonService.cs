using AeroSentry.Api.DTOs;

namespace AeroSentry.Api.Services;

// A photo as it arrives from the app.
public record PhotoUpload(byte[] Bytes, string ContentType);

public interface IPersonService
{
    Task<Result<PersonResponse>> EnrollAsync(Guid adminId, Guid propertyId, EnrollPersonRequest request, PhotoUpload front, PhotoUpload left, PhotoUpload right);
    Task<Result<List<PersonResponse>>> ListAsync(Guid adminId, Guid propertyId, string? category);
    Task<Result<PersonResponse>> GetAsync(Guid adminId, Guid propertyId, Guid personId);
    Task<Result<PersonResponse>> UpdateAsync(Guid adminId, Guid propertyId, Guid personId, UpdatePersonRequest request);
    Task<Result<PersonResponse>> SetAccessAsync(Guid adminId, Guid propertyId, Guid personId, bool disabled);
    Task<Result<bool>> DeleteAsync(Guid adminId, Guid propertyId, Guid personId);
    Task<Result<PhotoUpload>> GetPhotoAsync(Guid adminId, Guid propertyId, Guid personId, string angle);

    // Is this picture good enough to enrol with (exactly one face, big enough)? For the app to check each photo as it is taken.
    Task<Result<PhotoCheckResponse>> CheckPhotoAsync(Guid adminId, Guid propertyId, byte[] image);

    // Who is in this picture? Compares every face with the enrolled people of the property.
    Task<Result<IdentifyResponse>> IdentifyAsync(Guid adminId, Guid propertyId, byte[] image);

    // Used by the detection bridge when an alarm is raised: is the person at the wall an AUTHORIZED enrolled person?
    // box = the person's box [x1, y1, x2, y2] as fractions of the picture (null = any face). Best effort: returns null when
    // nobody matches, nobody is enrolled, or the face service is down. It never throws and never changes the alarm.
    Task<RecognizedPersonDto?> FindAuthorizedAsync(Guid propertyId, byte[] jpeg, double[]? box);
}
