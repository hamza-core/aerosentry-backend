using AeroSentry.Api.Data;
using AeroSentry.Api.Data.Entities;
using AeroSentry.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AeroSentry.Api.Services;

// =====================================================================
// PropertyService  (the "Service" layer)
//
// WHAT IT DOES: all the real work for properties. It talks to the database
//               through _db (AppDbContext). The controller only calls these methods.
//
// REQUEST FLOW: PropertiesController -> PropertyService -> AppDbContext -> SQL Server
//
// RULE USED EVERYWHERE: an Admin only touches properties where
//               OwnerAdminId == his own id. If the property is somebody else's we
//               behave as if it does not exist (return null / false -> controller sends 404).
//
// IF THE TEACHER ASKS YOU TO...
//   - add a field to Property (e.g. "City"):
//       1) Data/Entities/Property.cs   -> add the property
//       2) DTOs/PropertyRequest.cs     -> add it (what the client sends)
//       3) DTOs/PropertyResponse.cs    -> add it (what the client gets back)
//       4) this file                   -> copy it in CreateAsync, UpdateAsync and MakeResponse
//       5) terminal: dotnet ef migrations add AddCity   then   dotnet ef database update
//   - filter the list (e.g. by name): change the query in ListAsync (add a .Where(...))
// =====================================================================
public class PropertyService
{
    private readonly AppDbContext _db;   // our connection to the database

    // ASP.NET gives us the AppDbContext automatically (dependency injection).
    public PropertyService(AppDbContext db)
    {
        _db = db;
    }

    // ------------------------------------------------------------------
    // CREATE a property.  adminId = the logged-in admin (comes from the token).
    // ------------------------------------------------------------------
    public async Task<PropertyResponse> CreateAsync(Guid adminId, PropertyRequest request)
    {
        // Step 1: build a new Property object from what the client sent.
        Property property = new Property();
        property.Id = Guid.NewGuid();
        property.OwnerAdminId = adminId;               // the server sets the owner, never the client
        property.Name = request.Name.Trim();
        property.Address = request.Address.Trim();
        property.Latitude = request.Latitude;
        property.Longitude = request.Longitude;

        // Step 2: tell EF Core we want to insert it (nothing is in the database yet).
        _db.Properties.Add(property);

        // Step 3: SaveChangesAsync runs the INSERT statement.
        await _db.SaveChangesAsync();

        // Step 4: convert to the response shape and return it.
        return MakeResponse(property);
    }

    // ------------------------------------------------------------------
    // LIST properties.
    //   Admin -> properties he owns.   Guard -> properties he is assigned to.
    // ------------------------------------------------------------------
    public async Task<List<PropertyResponse>> ListAsync(Guid userId, string role)
    {
        // Get the query that already knows which properties this user may see.
        IQueryable<Property> query = PropertiesThisUserCanSee(userId, role);

        // Run it: sort by name, ask the database, get a list back.
        List<Property> properties = await query.OrderBy(p => p.Name).ToListAsync();

        // Convert every Property into a PropertyResponse.
        List<PropertyResponse> result = new List<PropertyResponse>();
        foreach (Property p in properties)
        {
            result.Add(MakeResponse(p));
        }
        return result;
    }

    // ------------------------------------------------------------------
    // GET one property by id.  Returns null if it does not exist or is not allowed.
    // ------------------------------------------------------------------
    public async Task<PropertyResponse?> GetAsync(Guid userId, string role, Guid id)
    {
        IQueryable<Property> query = PropertiesThisUserCanSee(userId, role);

        // FirstOrDefaultAsync = "give me the first match, or null if none".
        Property? property = await query.FirstOrDefaultAsync(p => p.Id == id);

        if (property == null)
        {
            return null;
        }
        return MakeResponse(property);
    }

    // ------------------------------------------------------------------
    // UPDATE a property (Admin only, his own).  Returns null if not found.
    // ------------------------------------------------------------------
    public async Task<PropertyResponse?> UpdateAsync(Guid adminId, Guid id, PropertyRequest request)
    {
        // Find the property, but only if it belongs to this admin.
        Property? property = await _db.Properties
            .FirstOrDefaultAsync(p => p.Id == id && p.OwnerAdminId == adminId);

        if (property == null)
        {
            return null;   // not found, or not his
        }

        // Change the values. EF Core notices the changes by itself ("change tracking").
        property.Name = request.Name.Trim();
        property.Address = request.Address.Trim();
        property.Latitude = request.Latitude;
        property.Longitude = request.Longitude;

        // SaveChangesAsync runs an UPDATE for the changed columns.
        await _db.SaveChangesAsync();

        return MakeResponse(property);
    }

    // ------------------------------------------------------------------
    // DELETE a property (Admin only, his own).  true = deleted, false = not found.
    // Its guard assignments are deleted automatically by the database (cascade).
    // ------------------------------------------------------------------
    public async Task<bool> DeleteAsync(Guid adminId, Guid id)
    {
        Property? property = await _db.Properties
            .FirstOrDefaultAsync(p => p.Id == id && p.OwnerAdminId == adminId);

        if (property == null)
        {
            return false;
        }

        _db.Properties.Remove(property);   // mark for deletion
        await _db.SaveChangesAsync();      // runs the DELETE
        return true;
    }

    // ==================================================================
    // GUARD ASSIGNMENT  (which guards work at which property)
    // The PropertyGuards table has one row per (property, guard) pair.
    // ==================================================================

    // List the guards assigned to one property.  Returns null if the property is not his.
    public async Task<List<GuardResponse>?> ListGuardsAsync(Guid adminId, Guid propertyId)
    {
        // First check the property exists and belongs to this admin.
        bool propertyIsHis = await _db.Properties
            .AnyAsync(p => p.Id == propertyId && p.OwnerAdminId == adminId);
        if (propertyIsHis == false)
        {
            return null;
        }

        // Get the assignment rows for this property, together with each guard (Include).
        List<PropertyGuard> links = await _db.PropertyGuards
            .Include(pg => pg.Guard)
            .Where(pg => pg.PropertyId == propertyId)
            .OrderBy(pg => pg.Guard.FullName)
            .ToListAsync();

        List<GuardResponse> result = new List<GuardResponse>();
        foreach (PropertyGuard link in links)
        {
            result.Add(GuardService.MakeGuardResponse(link.Guard));
        }
        return result;
    }

    // Assign a guard to a property.  false = the property or the guard is not his.
    public async Task<bool> AssignGuardAsync(Guid adminId, Guid propertyId, Guid guardId)
    {
        // The property must belong to this admin ...
        bool propertyIsHis = await _db.Properties
            .AnyAsync(p => p.Id == propertyId && p.OwnerAdminId == adminId);

        // ... and the guard must be a guard that this admin created.
        bool guardIsHis = await _db.Users
            .AnyAsync(u => u.Id == guardId && u.Role == "Guard" && u.OwnerAdminId == adminId);

        if (propertyIsHis == false || guardIsHis == false)
        {
            return false;
        }

        // If he is already assigned there is nothing to do (calling twice is harmless).
        bool alreadyAssigned = await _db.PropertyGuards
            .AnyAsync(pg => pg.PropertyId == propertyId && pg.GuardId == guardId);

        if (alreadyAssigned == false)
        {
            PropertyGuard link = new PropertyGuard();
            link.PropertyId = propertyId;
            link.GuardId = guardId;
            _db.PropertyGuards.Add(link);
            await _db.SaveChangesAsync();
        }
        return true;
    }

    // Remove a guard from a property.  false = the property is not his.
    public async Task<bool> UnassignGuardAsync(Guid adminId, Guid propertyId, Guid guardId)
    {
        bool propertyIsHis = await _db.Properties
            .AnyAsync(p => p.Id == propertyId && p.OwnerAdminId == adminId);
        if (propertyIsHis == false)
        {
            return false;
        }

        PropertyGuard? link = await _db.PropertyGuards
            .FirstOrDefaultAsync(pg => pg.PropertyId == propertyId && pg.GuardId == guardId);

        if (link != null)
        {
            _db.PropertyGuards.Remove(link);
            await _db.SaveChangesAsync();
        }
        return true;
    }

    // ==================================================================
    // HELPERS (private = only used inside this file)
    // ==================================================================

    // The ONE place that decides which properties a user may see.
    // List and Get both use it, so the rule can never be different between them.
    //   Admin -> properties where he is the owner
    //   Guard -> properties where a PropertyGuards row exists for him
    private IQueryable<Property> PropertiesThisUserCanSee(Guid userId, string role)
    {
        if (role == "Admin")
        {
            return _db.Properties.Where(p => p.OwnerAdminId == userId);
        }
        else
        {
            return _db.Properties.Where(p => p.PropertyGuards.Any(pg => pg.GuardId == userId));
        }
    }

    // Copy a Property (database shape) into a PropertyResponse (what the client sees).
    private PropertyResponse MakeResponse(Property p)
    {
        PropertyResponse response = new PropertyResponse();
        response.Id = p.Id;
        response.Name = p.Name;
        response.Address = p.Address;
        response.Latitude = p.Latitude;
        response.Longitude = p.Longitude;
        response.CreatedAtUtc = p.CreatedAtUtc;
        return response;
    }
}
