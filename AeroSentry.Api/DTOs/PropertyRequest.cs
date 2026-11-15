using System.ComponentModel.DataAnnotations;

namespace AeroSentry.Api.DTOs;

// WHAT THE CLIENT SENDS when creating (POST) or updating (PUT) a property.
// The attributes in [brackets] are validation rules. ASP.NET checks them BEFORE our code
// runs, and answers 400 automatically if a rule is broken.
public class PropertyRequest
{
    // [Required]        -> must be sent and not empty
    // [StringLength(200)] -> at most 200 characters
    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(300)]
    public string Address { get; set; } = string.Empty;

    // double? (with ?) means "optional": the client may leave it out (null).
    // [Range(-90, 90)] -> if it is sent, it must be between -90 and 90.
    [Range(-90, 90)]
    public double? Latitude { get; set; }

    [Range(-180, 180)]
    public double? Longitude { get; set; }
}
