using AeroSentry.Api.Data;
using AeroSentry.Api.Data.Entities;
using AeroSentry.Api.DTOs;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AeroSentry.Api.Services;

// =====================================================================
// GuardService  (the "Service" layer)
//
// WHAT IT DOES: create, list, suspend and delete guard accounts.
//               A guard is a row in the Users table with Role = "Guard".
//
// REQUEST FLOW: GuardsController -> GuardService -> AppDbContext -> SQL Server
//
// OWNERSHIP RULE: every method receives adminId (the logged-in admin) and only
//               touches guards where OwnerAdminId == adminId. So Admin A can never
//               see, suspend or delete Admin B's guards.
//
// IF THE TEACHER ASKS YOU TO...
//   - add a field to a guard: Data/Entities/User.cs, DTOs/GuardResponse.cs,
//     MakeGuardResponse() below, then migrations add + database update
//   - add another filter to the list: add another "if" with a .Where(...) in ListAsync
// =====================================================================
public class GuardService
{
    private readonly AppDbContext _db;
    private readonly PasswordHasher<User> _hasher = new PasswordHasher<User>();   // hashes passwords

    public GuardService(AppDbContext db)
    {
        _db = db;
    }

    // ------------------------------------------------------------------
    // CREATE a guard.  Returns null if the email is already used.
    // ------------------------------------------------------------------
    public async Task<GuardResponse?> CreateAsync(Guid adminId, RegisterRequest request)
    {
        // Emails are stored in lower case so "A@x.com" and "a@x.com" are the same.
        string email = request.Email.Trim().ToLowerInvariant();

        // Is there already a user with this email?
        bool emailTaken = await _db.Users.AnyAsync(u => u.Email == email);
        if (emailTaken)
        {
            return null;
        }

        User guard = new User();
        guard.Id = Guid.NewGuid();
        guard.FullName = request.FullName.Trim();
        guard.Email = email;
        guard.PhoneNumber = request.PhoneNumber;
        guard.Role = "Guard";             // the server decides the role, never the client
        guard.OwnerAdminId = adminId;     // this guard belongs to the admin who created him

        // Never store the real password: store its hash.
        guard.PasswordHash = _hasher.HashPassword(guard, request.Password);

        _db.Users.Add(guard);
        await _db.SaveChangesAsync();     // INSERT

        return MakeGuardResponse(guard);
    }

    // ------------------------------------------------------------------
    // LIST this admin's guards.  status = null (everyone), "Active" or "Suspended".
    // The controller already checked that status is one of those values.
    // ------------------------------------------------------------------
    public async Task<List<GuardResponse>> ListAsync(Guid adminId, string? status = null)
    {
        // Start with: all guards owned by this admin.
        IQueryable<User> query = _db.Users
            .Where(u => u.Role == "Guard" && u.OwnerAdminId == adminId);

        // Add a filter only if the client asked for one.
        // Nothing has run yet: the filters are added to the SQL query, which is
        // executed once, at ToListAsync().
        if (status != null)
        {
            bool wantActive = status.Equals("Active", StringComparison.OrdinalIgnoreCase);
            query = query.Where(u => u.IsActive == wantActive);
        }

        List<User> guards = await query.OrderBy(u => u.FullName).ToListAsync();

        List<GuardResponse> result = new List<GuardResponse>();
        foreach (User guard in guards)
        {
            result.Add(MakeGuardResponse(guard));
        }
        return result;
    }

    // ------------------------------------------------------------------
    // SUSPEND or ACTIVATE a guard.  Returns null if this admin has no such guard.
    // ------------------------------------------------------------------
    public async Task<GuardResponse?> SetStatusAsync(Guid adminId, Guid id, string status)
    {
        User? guard = await _db.Users.FirstOrDefaultAsync(
            u => u.Id == id && u.Role == "Guard" && u.OwnerAdminId == adminId);

        if (guard == null)
        {
            return null;
        }

        // IsActive = true means "Active", false means "Suspended".
        if (status == "Active")
        {
            guard.IsActive = true;
        }
        else
        {
            guard.IsActive = false;
        }

        await _db.SaveChangesAsync();   // UPDATE

        return MakeGuardResponse(guard);
    }

    // ------------------------------------------------------------------
    // DELETE a guard.  true = deleted, false = not found.
    // His property assignments are removed automatically (cascade).
    // ------------------------------------------------------------------
    public async Task<bool> DeleteAsync(Guid adminId, Guid id)
    {
        User? guard = await _db.Users.FirstOrDefaultAsync(
            u => u.Id == id && u.Role == "Guard" && u.OwnerAdminId == adminId);

        if (guard == null)
        {
            return false;
        }

        _db.Users.Remove(guard);
        await _db.SaveChangesAsync();   // DELETE
        return true;
    }

    // ------------------------------------------------------------------
    // Copy a User (database shape) into a GuardResponse (what the client sees).
    // "public static" because PropertyService also uses it.
    // Note: PasswordHash is NOT copied, so it can never leak.
    // ------------------------------------------------------------------
    public static GuardResponse MakeGuardResponse(User user)
    {
        GuardResponse response = new GuardResponse();
        response.Id = user.Id;
        response.FullName = user.FullName;
        response.Email = user.Email;
        response.Role = user.Role;

        if (user.IsActive)
        {
            response.Status = "Active";
        }
        else
        {
            response.Status = "Suspended";
        }
        return response;
    }
}
