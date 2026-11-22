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

    // Used by the detection bridge when an alarm is raised. Looks for the faces of the people at the wall in the alarm frame and a few
    // earlier frames, and decides who is a TRUSTED authorized person:
    //   - a face counts only if it is wide enough (Face:MinRecognizeFaceWidth); it belongs to exactly ONE person (the box it is
    //     inside whose head position it is nearest to), so one face can never vouch for two people;
    //   - a person (track) is "cleared" only if ONE enrolled, still-authorized person matched on it in at least
    //     Face:MinMatchingFrames different frames, and nobody else matched on that track;
    //   - AllCleared is true only if EVERY person in the alarm frame is cleared (one unknown person means a full response).
    // frames: oldest first, the alarm frame LAST. Best effort: any problem (nobody enrolled, face service down) gives
    // "nobody recognised". It never throws and never changes the alarm.
    Task<AlarmRecognition> RecognizeForAlarmAsync(Guid propertyId, IReadOnlyList<RecognitionFrame> frames, int targetTrackId);
}

// A person in a picture, as the wall model reports it: a track id (stable while the person stays in view) and a box
// [x1, y1, x2, y2] as fractions of the picture.
public record TrackBox(int TrackId, double[] Box);

// One camera picture with the people the wall model found in it.
public record RecognitionFrame(byte[] Jpeg, IReadOnlyList<TrackBox> People);

// Person = the trusted authorized person for the alarm's target (null if none). AllCleared = everyone at the wall is trusted.
public record AlarmRecognition(RecognizedPersonDto? Person, bool AllCleared);
