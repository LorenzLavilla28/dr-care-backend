using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using DrCare.Api.Middleware;
using DrCare.Api.Security;
using DrCare.Application;
using DrCare.Application.Auth;
using DrCare.Application.Leads;
using DrCare.Application.Processes;
using DrCare.Application.Settings;
using DrCare.Application.Reports;
using DrCare.Application.Users;
using DrCare.Application.Tasks;
using DrCare.Application.Workflows;
using DrCare.Infrastructure;
using DrCare.Infrastructure.Persistence;
using DrCare.Infrastructure.Security;
using DrCare.Infrastructure.Storage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Keep real development credentials in an ignored local override. The tracked
// Development settings remain safe to commit and the override is optional.
if (builder.Environment.IsDevelopment())
    builder.Configuration.AddJsonFile("appsettings.Development.local.json", optional: true, reloadOnChange: true);

var configuredStorageProvider = builder.Configuration["Storage:Provider"]?.Trim();
if (!builder.Environment.IsDevelopment() &&
    (configuredStorageProvider?.Equals("Local", StringComparison.OrdinalIgnoreCase) == true ||
     string.IsNullOrWhiteSpace(builder.Configuration[$"{S3Options.SectionName}:BucketName"])))
    throw new InvalidOperationException("Production storage must use S3 with Storage:S3:BucketName configured.");

builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 2 * 1024 * 1024);
builder.Services.AddHttpContextAccessor();
builder.Services.AddControllers().AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ILeadService, LeadService>();
builder.Services.AddScoped<IProcessService, ProcessService>();
builder.Services.AddScoped<IContractSigningService, ContractSigningService>();
builder.Services.AddScoped<IPreLaunchService, PreLaunchService>();
builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ITaskService, TaskService>();
builder.Services.AddScoped<IWorkflowService, WorkflowService>();

var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
if (Encoding.UTF8.GetByteCount(jwt.SigningKey) < 32) throw new InvalidOperationException("Jwt:SigningKey must be at least 32 bytes.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = jwt.Issuer,
        ValidateAudience = true,
        ValidAudience = jwt.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = "name",
        RoleClaimType = System.Security.Claims.ClaimTypes.Role
    };
});
builder.Services.AddAuthorization();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddPolicy("frontend", policy =>
{
    policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
}));

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("api", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst("sub")?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("signing", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

builder.Services.AddHealthChecks().AddDbContextCheck<ApplicationDbContext>();

var app = builder.Build();
await app.Services.InitializeDevelopmentDatabaseAsync(app.Configuration, app.Environment);

app.UseMiddleware<ApiExceptionMiddleware>();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    context.Response.Headers["Cache-Control"] = "no-store";
    await next();
});

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseCors("frontend");
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready");
app.MapHealthChecks("/api/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/api/health/ready");
app.MapControllers();

app.Run();

public partial class Program;
