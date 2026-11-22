using Scalar.AspNetCore;
using AeroSentry.Api.Data;
using AeroSentry.Api.Services;
using AeroSentry.Api.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<GuardService>();
builder.Services.AddScoped<PropertyService>();
builder.Services.AddScoped<ICameraService, CameraService>();
builder.Services.AddScoped<ILaserGridService, LaserGridService>();
builder.Services.AddScoped<IWallService, WallService>();
builder.Services.AddScoped<IWallCellService, WallCellService>();

// The ML service is a separate local program (port 8010). One typed HttpClient talks to it.
builder.Services.AddHttpClient<IMlClient, MlClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Ml:BaseUrl"] ?? "http://localhost:8010");
    client.Timeout = TimeSpan.FromSeconds(15);
});
// A separate typed client takes a still picture from IP Webcam phones (for drawing the wall annotation).
builder.Services.AddHttpClient<ICameraSnapshotClient, CameraSnapshotClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(6);
    client.MaxResponseContentBufferSize = 10 * 1024 * 1024;
});
builder.Services.AddSingleton<DetectionSessions>();   // one shared wall -> ML session map
builder.Services.AddScoped<IDetectionService, DetectionService>();

// Alerts: stored in the database, pushed live over SignalR (hub at /hubs/alerts).
builder.Services.AddScoped<IAlertService, AlertService>();
builder.Services.AddScoped<IAlertNotifier, AlertNotifier>();
builder.Services.AddSignalR();
builder.Services.AddSingleton<ConnectionTracker>();   // open hub connections per user, so they can be closed
builder.Services.AddSingleton<IUserIdProvider, SubUserIdProvider>();   // "send to user X" = the token's sub claim

// CORS: browsers (the ReactJS client) may only call the API from origins listed here.
// Phones and desktop apps ignore CORS. Add the demo machine's address in Cors:Origins when running on a LAN.
var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
                  ?? new[] { "http://localhost:3000", "http://localhost:5173" };
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()
          // A browser may only read these two response headers (the snapshot's size) if we say so.
          .WithExposedHeaders("X-Frame-Width", "X-Frame-Height")));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            RoleClaimType = "role"
        };

        options.Events = new JwtBearerEvents
        {
            // Browsers cannot put a header on a WebSocket, so for the SignalR hub the token comes
            // in the query string (?access_token=...). Only accepted on /hubs paths.
            OnMessageReceived = context =>
            {
                var token = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    context.Token = token;
                return Task.CompletedTask;
            },

            OnTokenValidated = async context =>
            {
                var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var sub = context.Principal?.FindFirst("sub")?.Value;

                if (!Guid.TryParse(sub, out var id) ||
                    !await db.Users.AnyAsync(u => u.Id == id && u.IsActive))
                {
                    context.Fail("User is inactive or no longer exists.");
                }
            }
        };
    });


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{    
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<AlertsHub>("/hubs/alerts", options =>
{
    // When the token of an open connection expires, close the connection (the app logs in again).
    options.CloseOnAuthenticationExpiration = true;
});

app.Run();
