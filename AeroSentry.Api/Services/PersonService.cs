using AeroSentry.Api.Data;
using AeroSentry.Api.Data.Entities;
using AeroSentry.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AeroSentry.Api.Services;

// Admins manage the authorized people of properties they own. The face service only turns a picture into numbers;
// everything else (who is enrolled, who matches, who is still allowed) is decided here.
public class PersonService : IPersonService
{
    private const int MaxPhotoBytes = 10 * 1024 * 1024;

    private readonly AppDbContext _db;
    private readonly IFaceClient _faces;
    private readonly IConfiguration _config;
    private readonly ILogger<PersonService> _logger;

    public PersonService(AppDbContext db, IFaceClient faces, IConfiguration config, ILogger<PersonService> logger)
    {
        _db = db;
        _faces = faces;
        _config = config;
        _logger = logger;
    }

    // From the learning scripts: 0.45 was the value of the multi-face script. Different people scored 0.00 to 0.25 against each
    // other and the same person 0.43 to 0.86 on the project's own photos. A security whitelist should rather miss a weak photo
    // than let a stranger in, so this leans strict.
    private double MatchThreshold => _config.GetValue<double?>("Face:MatchThreshold") ?? 0.45;
    // The enrolment script needed a face wider than 100 px.
    private int MinEnrollFaceWidth => _config.GetValue<int?>("Face:MinEnrollFaceWidth") ?? 100;
    // The three photos must look like one person: similarity between each pair of them.
    private double MinPhotoSimilarity => _config.GetValue<double?>("Face:MinPhotoSimilarity") ?? 0.30;
    // On an alarm, a face smaller than this is ignored as "not a visible face" (a tiny face gives a weak, unreliable embedding).
    private int MinRecognizeFaceWidth => _config.GetValue<int?>("Face:MinRecognizeFaceWidth") ?? 60;
    // A person is only cleared when the same enrolled person matched in at least this many different frames.
    private int MinMatchingFrames => _config.GetValue<int?>("Face:MinMatchingFrames") ?? 2;
    // Surveillance frames have small faces: look harder than the default 640.
    private int SurveillanceDetSize => _config.GetValue<int?>("Face:SurveillanceDetSize") ?? 960;

    public async Task<Result<PersonResponse>> EnrollAsync(
        Guid adminId, Guid propertyId, EnrollPersonRequest request, PhotoUpload front, PhotoUpload left, PhotoUpload right)
    {
        if (!await _db.OwnsProperty(adminId, propertyId))
            return Result<PersonResponse>.NotFound("Property not found");

        var uploads = new[] { (PersonAngle.Front, front), (PersonAngle.Left, left), (PersonAngle.Right, right) };
        var faces = new List<(string Angle, PhotoUpload Upload, FaceInfo Face)>();

        // 1. Each photo must show exactly one face, big enough.
        foreach (var (angle, upload) in uploads)
        {
            if (upload.Bytes.Length == 0 || upload.Bytes.Length > MaxPhotoBytes)
                return Result<PersonResponse>.BadRequest($"The {angle} photo must be a picture of at most 10 MB");

            FaceAnalysis analysis;
            try
            {
                analysis = await _faces.AnalyzeAsync(upload.Bytes);
            }
            catch (FaceRejectedException ex)
            {
                return Result<PersonResponse>.BadRequest($"The {angle} photo: {ex.Message}");
            }
            catch (FaceException ex)
            {
                return Result<PersonResponse>.Unavailable(ex.Message);
            }

            var problem = JudgePhoto(analysis, out var face);
            if (problem is not null)
                return Result<PersonResponse>.BadRequest($"The {angle} photo: {problem}");

            faces.Add((angle, upload, face!));
        }

        // 2. The three photos must be of the same person.
        var vectors = faces.Select(f => f.Face.Embedding).ToList();
        for (var i = 0; i < vectors.Count; i++)
        {
            for (var j = i + 1; j < vectors.Count; j++)
            {
                if (FaceMath.Dot(vectors[i], vectors[j]) < MinPhotoSimilarity)
                    return Result<PersonResponse>.BadRequest(
                        $"The {faces[i].Angle} and {faces[j].Angle} photos do not look like the same person");
            }
        }

        // 3. Not already enrolled (any status), so a person never exists twice.
        var mean = FaceMath.MeanNormalized(vectors);
        var existing = await _db.AuthorizedPeople.Where(p => p.PropertyId == propertyId).ToListAsync();
        foreach (var other in existing)
        {
            if (FaceMath.Dot(mean, FaceMath.FromBytes(other.MeanEmbedding)) >= MatchThreshold)
                return Result<PersonResponse>.Conflict($"This face is already enrolled as '{other.Name}'");
        }

        var person = new AuthorizedPerson
        {
            PropertyId = propertyId,
            Name = request.Name.Trim(),
            Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            Category = request.Category,
            ValidUntil = request.ValidUntil,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            MeanEmbedding = FaceMath.ToBytes(mean)
        };
        foreach (var (angle, upload, face) in faces)
        {
            person.Photos.Add(new PersonPhoto
            {
                Angle = angle,
                ContentType = string.IsNullOrWhiteSpace(upload.ContentType) ? "image/jpeg" : upload.ContentType,
                Image = upload.Bytes,
                Embedding = FaceMath.ToBytes(face.Embedding),
                FaceWidth = face.Width
            });
        }

        _db.AuthorizedPeople.Add(person);
        await _db.SaveChangesAsync();
        return Result<PersonResponse>.Ok(ToResponse(person));
    }

    public async Task<Result<List<PersonResponse>>> ListAsync(Guid adminId, Guid propertyId, string? category)
    {
        if (category is not null && !PersonCategory.All.Contains(category))
            return Result<List<PersonResponse>>.BadRequest($"category must be one of: {string.Join(", ", PersonCategory.All)}");
        if (!await _db.OwnsProperty(adminId, propertyId))
            return Result<List<PersonResponse>>.NotFound("Property not found");

        var query = _db.AuthorizedPeople.Where(p => p.PropertyId == propertyId);
        if (category is not null)
            query = query.Where(p => p.Category == category);

        // The pictures are not loaded for a list (they are big): a person always has all three, so the angles need no query.
        var people = await query.OrderBy(p => p.Category).ThenBy(p => p.Name).ToListAsync();
        return Result<List<PersonResponse>>.Ok(people.Select(ToResponse).ToList());
    }

    public async Task<Result<PersonResponse>> GetAsync(Guid adminId, Guid propertyId, Guid personId)
    {
        var found = await FindAsync(adminId, propertyId, personId);
        return found.Status == ResultStatus.Success ? Result<PersonResponse>.Ok(ToResponse(found.Value!)) : Result<PersonResponse>.NotFound(found.Message);
    }

    public async Task<Result<PersonResponse>> UpdateAsync(Guid adminId, Guid propertyId, Guid personId, UpdatePersonRequest request)
    {
        var found = await FindAsync(adminId, propertyId, personId);
        if (found.Status != ResultStatus.Success)
            return Result<PersonResponse>.NotFound(found.Message);

        var person = found.Value!;
        person.Name = request.Name.Trim();
        person.Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
        person.Category = request.Category;
        person.ValidUntil = request.ValidUntil;
        person.Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        await _db.SaveChangesAsync();
        return Result<PersonResponse>.Ok(ToResponse(person));
    }

    public async Task<Result<PersonResponse>> SetAccessAsync(Guid adminId, Guid propertyId, Guid personId, bool disabled)
    {
        var found = await FindAsync(adminId, propertyId, personId);
        if (found.Status != ResultStatus.Success)
            return Result<PersonResponse>.NotFound(found.Message);

        found.Value!.AccessDisabled = disabled;
        await _db.SaveChangesAsync();
        return Result<PersonResponse>.Ok(ToResponse(found.Value));
    }

    public async Task<Result<bool>> DeleteAsync(Guid adminId, Guid propertyId, Guid personId)
    {
        var found = await FindAsync(adminId, propertyId, personId);
        if (found.Status != ResultStatus.Success)
            return Result<bool>.NotFound(found.Message);

        _db.AuthorizedPeople.Remove(found.Value!);   // the photos and the face numbers go with it (cascade): biometric data is deleted for good
        await _db.SaveChangesAsync();
        return Result<bool>.Ok(true);
    }

    public async Task<Result<PhotoUpload>> GetPhotoAsync(Guid adminId, Guid propertyId, Guid personId, string angle)
    {
        if (!PersonAngle.All.Contains(angle))
            return Result<PhotoUpload>.BadRequest($"angle must be one of: {string.Join(", ", PersonAngle.All)}");
        if (!await _db.OwnsProperty(adminId, propertyId))
            return Result<PhotoUpload>.NotFound("Property not found");

        var photo = await _db.PersonPhotos
            .Where(ph => ph.PersonId == personId && ph.Angle == angle && ph.Person.PropertyId == propertyId)
            .Select(ph => new PhotoUpload(ph.Image, ph.ContentType))
            .FirstOrDefaultAsync();

        return photo is null ? Result<PhotoUpload>.NotFound("Photo not found") : Result<PhotoUpload>.Ok(photo);
    }

    public async Task<Result<PhotoCheckResponse>> CheckPhotoAsync(Guid adminId, Guid propertyId, byte[] image)
    {
        if (!await _db.OwnsProperty(adminId, propertyId))
            return Result<PhotoCheckResponse>.NotFound("Property not found");
        if (image.Length == 0 || image.Length > MaxPhotoBytes)
            return Result<PhotoCheckResponse>.BadRequest("The photo must be a picture of at most 10 MB");

        try
        {
            var analysis = await _faces.AnalyzeAsync(image);
            var problem = JudgePhoto(analysis, out var face);
            return Result<PhotoCheckResponse>.Ok(new PhotoCheckResponse
            {
                Ok = problem is null,
                FaceCount = analysis.Faces.Count,
                FaceWidth = face?.Width ?? 0,
                Message = problem ?? "Good photo"
            });
        }
        catch (FaceRejectedException ex)
        {
            return Result<PhotoCheckResponse>.BadRequest(ex.Message);
        }
        catch (FaceException ex)
        {
            return Result<PhotoCheckResponse>.Unavailable(ex.Message);
        }
    }

    public async Task<Result<IdentifyResponse>> IdentifyAsync(Guid adminId, Guid propertyId, byte[] image)
    {
        if (!await _db.OwnsProperty(adminId, propertyId))
            return Result<IdentifyResponse>.NotFound("Property not found");
        if (image.Length == 0 || image.Length > MaxPhotoBytes)
            return Result<IdentifyResponse>.BadRequest("The photo must be a picture of at most 10 MB");

        FaceAnalysis analysis;
        try
        {
            analysis = await _faces.AnalyzeAsync(image, SurveillanceDetSize);
        }
        catch (FaceRejectedException ex)
        {
            return Result<IdentifyResponse>.BadRequest(ex.Message);
        }
        catch (FaceException ex)
        {
            return Result<IdentifyResponse>.Unavailable(ex.Message);
        }

        var gallery = await LoadGalleryAsync(propertyId, onlyAuthorized: false);
        var response = new IdentifyResponse { FaceCount = analysis.Faces.Count };

        foreach (var face in analysis.Faces)
        {
            var (best, score) = BestMatch(gallery, face.Embedding);
            var matched = best is not null && score >= MatchThreshold;
            response.Faces.Add(new IdentifiedFaceDto
            {
                Box = face.Box,
                FaceWidth = face.Width,
                DetectionScore = face.DetScore,
                BestScore = Math.Round(score, 4),
                Matched = matched,
                Person = matched ? ToResponse(best!.Person) : null,
                IsAuthorized = matched && StatusOf(best!.Person) == PersonStatus.Authorized
            });
        }

        return Result<IdentifyResponse>.Ok(response);
    }

    public async Task<AlarmRecognition> RecognizeForAlarmAsync(Guid propertyId, IReadOnlyList<RecognitionFrame> frames, int targetTrackId)
    {
        var nobody = new AlarmRecognition(null, false);
        try
        {
            var gallery = await LoadGalleryAsync(propertyId, onlyAuthorized: true);
            if (gallery.Count == 0 || frames.Count == 0)
                return nobody;   // nobody enrolled (or still authorized): no need to look at the pictures at all

            // For every person (track), every frame where a wide-enough face inside the person's box matched an enrolled person.
            var matches = new Dictionary<int, List<(int Frame, GalleryEntry Entry, double Score)>>();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));

            for (var f = 0; f < frames.Count; f++)
            {
                if (frames[f].People.Count == 0)
                    continue;

                var analysis = await _faces.AnalyzeAsync(frames[f].Jpeg, SurveillanceDetSize, timeout.Token);

                // Every face belongs to exactly ONE person, so one face can never vouch for two people.
                foreach (var (face, trackId) in AssignFacesToPeople(analysis, frames[f].People))
                {
                    var (best, score) = BestMatch(gallery, face.Embedding);
                    if (best is null || score < MatchThreshold)
                        continue;

                    if (!matches.TryGetValue(trackId, out var list))
                        matches[trackId] = list = new();
                    list.Add((f, best, score));
                }
            }

            // Cleared = ONE enrolled person matched in enough different frames, and nobody else matched on that track.
            var cleared = new Dictionary<int, RecognizedPersonDto>();
            foreach (var (track, list) in matches)
            {
                var groups = list.GroupBy(m => m.Entry.Person.Id).ToList();
                if (groups.Count != 1)
                    continue;   // two different people on one track: do not trust either

                var group = groups[0];
                if (group.Select(m => m.Frame).Distinct().Count() < MinMatchingFrames)
                    continue;

                var person = group.First().Entry.Person;
                cleared[track] = new RecognizedPersonDto
                {
                    Id = person.Id, Name = person.Name, Category = person.Category, Score = Math.Round(group.Average(m => m.Score), 4)
                };
            }

            // Everyone in the alarm frame (the last one) must be cleared, otherwise the full response stays on.
            var alarmFrame = frames[^1];
            var allCleared = alarmFrame.People.Count > 0 && alarmFrame.People.All(p => cleared.ContainsKey(p.TrackId));
            return new AlarmRecognition(cleared.GetValueOrDefault(targetTrackId), allCleared);
        }
        catch (Exception ex)
        {
            // Face recognition is an extra. It must never stop an alarm from being raised.
            _logger.LogWarning(ex, "Face recognition for an alarm was skipped");
            return nobody;
        }
    }

    // Gives every wide-enough face to exactly one person. Person boxes can overlap (people close together, one in front of the
    // other), and then the same face lies inside several boxes. If it counted for all of them, ONE authorized person's face
    // could clear a stranger standing next to or behind them. So the face goes to the one person it is nearest to: the box that
    // contains the face's centre and whose head position (top middle) is closest. A person left without a face stays unknown.
    private List<(FaceInfo Face, int TrackId)> AssignFacesToPeople(FaceAnalysis analysis, IReadOnlyList<TrackBox> people)
    {
        var owners = new List<(FaceInfo, int)>();
        if (analysis.ImageWidth <= 0 || analysis.ImageHeight <= 0)
            return owners;

        foreach (var face in analysis.Faces.Where(f => f.Width >= MinRecognizeFaceWidth))
        {
            var cx = (face.Box[0] + face.Box[2]) / 2.0 / analysis.ImageWidth;
            var cy = (face.Box[1] + face.Box[3]) / 2.0 / analysis.ImageHeight;

            var owner = people
                .Where(p => p.Box.Length == 4 && cx >= p.Box[0] && cx <= p.Box[2] && cy >= p.Box[1] && cy <= p.Box[3])
                .OrderBy(p =>
                {
                    var width = Math.Max(p.Box[2] - p.Box[0], 1e-6);
                    var height = Math.Max(p.Box[3] - p.Box[1], 1e-6);
                    var headX = (p.Box[0] + p.Box[2]) / 2;
                    var headY = p.Box[1] + 0.12 * height;
                    return Math.Abs(cx - headX) / width + Math.Abs(cy - headY) / height;
                })
                .ThenBy(p => p.TrackId)   // an exact tie always resolves the same way
                .FirstOrDefault();

            if (owner is not null)
                owners.Add((face, owner.TrackId));
        }
        return owners;
    }

    // ---- helpers ----

    private record GalleryEntry(AuthorizedPerson Person, float[] Mean);

    private async Task<List<GalleryEntry>> LoadGalleryAsync(Guid propertyId, bool onlyAuthorized)
    {
        var people = await _db.AuthorizedPeople.Where(p => p.PropertyId == propertyId).ToListAsync();
        return people
            .Where(p => !onlyAuthorized || StatusOf(p) == PersonStatus.Authorized)
            .Select(p => new GalleryEntry(p, FaceMath.FromBytes(p.MeanEmbedding)))
            .ToList();
    }

    private static (GalleryEntry? Best, double Score) BestMatch(List<GalleryEntry> gallery, float[] embedding)
    {
        GalleryEntry? best = null;
        var bestScore = 0.0;
        foreach (var entry in gallery)
        {
            var score = FaceMath.Dot(entry.Mean, embedding);
            if (best is null || score > bestScore)
            {
                best = entry;
                bestScore = score;
            }
        }
        return (best, bestScore);
    }

    // Null when the picture is fine, otherwise what is wrong with it.
    private string? JudgePhoto(FaceAnalysis analysis, out FaceInfo? face)
    {
        face = null;
        if (analysis.Faces.Count == 0)
            return "no face was found";
        if (analysis.Faces.Count > 1)
            return $"it must show exactly one face (found {analysis.Faces.Count})";

        face = analysis.Faces[0];
        if (face.Width < MinEnrollFaceWidth)
            return $"the face is too small ({face.Width} px wide, at least {MinEnrollFaceWidth} px needed): move closer";
        return null;
    }

    private async Task<Result<AuthorizedPerson>> FindAsync(Guid adminId, Guid propertyId, Guid personId)
    {
        if (!await _db.OwnsProperty(adminId, propertyId))
            return Result<AuthorizedPerson>.NotFound("Property not found");

        // The pictures are not loaded (they are big). A response never needs them.
        var person = await _db.AuthorizedPeople.FirstOrDefaultAsync(p => p.Id == personId && p.PropertyId == propertyId);
        return person is null ? Result<AuthorizedPerson>.NotFound("Person not found") : Result<AuthorizedPerson>.Ok(person);
    }

    public static string StatusOf(AuthorizedPerson p)
    {
        if (p.AccessDisabled)
            return PersonStatus.Disabled;
        if (p.ValidUntil is not null && p.ValidUntil < DateOnly.FromDateTime(DateTime.UtcNow))
            return PersonStatus.Expired;
        return PersonStatus.Authorized;
    }

    private static PersonResponse ToResponse(AuthorizedPerson p) => new()
    {
        Id = p.Id,
        PropertyId = p.PropertyId,
        Name = p.Name,
        Phone = p.Phone,
        Category = p.Category,
        ValidUntil = p.ValidUntil,
        Note = p.Note,
        Status = StatusOf(p),
        AccessDisabled = p.AccessDisabled,
        EnrolledAtUtc = p.EnrolledAtUtc,
        PhotoAngles = PersonAngle.All.ToList()   // enrolment always stores all three
    };
}
