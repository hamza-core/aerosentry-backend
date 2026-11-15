using AeroSentry.Api.Data;
using AeroSentry.Api.Data.Entities;
using AeroSentry.Api.DTOs;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AeroSentry.Api.Services;

public class GuardService
{
    private readonly AppDbContext _db;
    private readonly PasswordHasher<User> _hasher = new();

    public GuardService(AppDbContext db)
    {
        _db = db;
    }

    // Returns null when the email is already taken.
    public async Task<GuardResponse?> CreateAsync(RegisterRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        if (await _db.Users.AnyAsync(u => u.Email == email))
            return null;

        var guard = new User
        {
            Id = Guid.NewGuid(),
            FullName = request.FullName.Trim(),
            Email = email,
            Role = "Guard"
        };
        guard.PasswordHash = _hasher.HashPassword(guard, request.Password);

        _db.Users.Add(guard);
        await _db.SaveChangesAsync();

        return new GuardResponse
        {
            Id = guard.Id,
            FullName = guard.FullName,
            Email = guard.Email,
            Role = guard.Role,
            Status = guard.IsActive ? "Active" : "Suspended"
        };
    }
}