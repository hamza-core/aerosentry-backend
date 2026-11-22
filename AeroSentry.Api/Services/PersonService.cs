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

    public async Task<RecognizedPersonDto?> FindAuthorizedAsync(Guid propertyId, byte[] jpeg, double[]? box)
    {
        try
        {
            var gallery = await LoadGalleryAsync(propertyId, onlyAuthorized: true);
            if (gallery.Count == 0)
                return null;   // nobody enrolled: no need to look at the picture at all

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var analysis = await _faces.AnalyzeAsync(jpeg, SurveillanceDetSize, timeout.Token);

            RecognizedPersonDto? result = null;
            foreach (var face in analysis.Faces)
            {
                // Only the face that belongs to the person the alarm is about: its centre must lie inside that person's box.
                if (box is { Length: 4 } && analysis.ImageWidth > 0 && analysis.ImageHeight > 0)
                {
                    var cx = (face.Box[0] + face.Box[2]) / 2.0 / analysis.ImageWidth;
                    var cy = (face.Box[1] + face.Box[3]) / 2.0 / analysis.ImageHeight;
                    if (cx < box[0] || cx > box[2] || cy < box[1] || cy > box[3])
                        continue;
                }

                var (best, score) = BestMatch(gallery, face.Embedding);
                if (best is not null && score >= MatchThreshold && (result is null || score > result.Score))
                    result = new RecognizedPersonDto { Id = best.Person.Id, Name = best.Person.Name, Category = best.Person.Category, Score = Math.Round(score, 4) };
            }
            return result;
        }
        catch (Exception ex)
        {
            // Face recognition is an extra. It must never stop an alarm from being raised.
            _logger.LogWarning(ex, "Face recognition for an alarm was skipped");
            return null;
        }
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
