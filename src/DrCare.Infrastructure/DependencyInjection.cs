using DrCare.Application;
using DrCare.Application.Notifications;
using DrCare.Infrastructure.Persistence;
using DrCare.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Amazon;
using Amazon.S3;
using DrCare.Infrastructure.Storage;
using DrCare.Infrastructure.Documents;

namespace DrCare.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<S3Options>(configuration.GetSection(S3Options.SectionName));
        services.Configure<LocalStorageOptions>(configuration.GetSection(LocalStorageOptions.SectionName));
        services.Configure<DocumentRenderingOptions>(configuration.GetSection(DocumentRenderingOptions.SectionName));
        var storageProvider = configuration["Storage:Provider"]?.Trim();
        var useLocalStorage = storageProvider?.Equals("Local", StringComparison.OrdinalIgnoreCase) == true ||
            string.IsNullOrWhiteSpace(configuration[$"{S3Options.SectionName}:BucketName"]);
        if (useLocalStorage)
        {
            services.AddSingleton<LocalObjectStorage>();
            services.AddSingleton<ILocalObjectStorage>(sp => sp.GetRequiredService<LocalObjectStorage>());
            services.AddSingleton<IPrivateObjectStorage>(sp => sp.GetRequiredService<LocalObjectStorage>());
        }
        else
        {
            services.AddSingleton<IAmazonS3>(sp =>
            {
                var options = sp.GetRequiredService<IOptions<S3Options>>().Value;
                return new AmazonS3Client(new AmazonS3Config { RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region) });
            });
            services.AddScoped<IPrivateObjectStorage, S3ObjectStorage>();
        }
        services.AddScoped<IDocumentPdfRenderer, ChromiumDocumentPdfRenderer>();
        services.AddScoped<ILeadRepository, LeadRepository>();
        services.AddScoped<ITaskRepository, TaskRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IAuthTokenRepository, AuthTokenRepository>();
        services.AddScoped<IWorkflowRepository, WorkflowRepository>();
        services.AddScoped<IProcessRepository, ProcessRepository>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IPreLaunchRepository, PreLaunchRepository>();
        services.AddScoped<ISettingsRepository, SettingsRepository>();
        services.AddScoped<IReportingRepository, ReportingRepository>();
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        return services;
    }

    public static async Task InitializeDevelopmentDatabaseAsync(this IServiceProvider services, IConfiguration configuration, IHostEnvironment environment)
    {
        if (!environment.IsDevelopment()) return;
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();

        var password = configuration["DevelopmentAdminPassword"];
        if (string.IsNullOrWhiteSpace(password)) return;

        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordService>();
        var organizationId = Guid.Parse(configuration["DevelopmentOrganizationId"] ?? "00000000-0000-0000-0000-000000000001");
        var seedUsers = new[]
        {
            ("admin@drcare.local", "Maria Santos", DrCare.Domain.UserRole.MarketingAdmin),
            ("admin.2@drcare.local", "Sofia Mendoza", DrCare.Domain.UserRole.MarketingAdmin),
            ("marketing.agent@drcare.local", "Juan Dela Cruz", DrCare.Domain.UserRole.MarketingAgent),
            ("marketing.agent.2@drcare.local", "Daniel Cruz", DrCare.Domain.UserRole.MarketingAgent),
            ("general.manager@drcare.local", "Carlos Reyes", DrCare.Domain.UserRole.GeneralManager),
            ("general.manager.2@drcare.local", "Beatrice Lim", DrCare.Domain.UserRole.GeneralManager),
            ("finance@drcare.local", "Liza Bautista", DrCare.Domain.UserRole.Finance),
            ("finance.2@drcare.local", "Rafael Santos", DrCare.Domain.UserRole.Finance),
            ("admin.team@drcare.local", "Paolo Navarro", DrCare.Domain.UserRole.AdminTeam),
            ("admin.team.2@drcare.local", "Nina Garcia", DrCare.Domain.UserRole.AdminTeam),
            ("leadership@drcare.local", "Ana Villanueva", DrCare.Domain.UserRole.Leadership),
            ("leadership.2@drcare.local", "Victor Aquino", DrCare.Domain.UserRole.Leadership)
        };

        var seedEmails = seedUsers.Select(seed => seed.Item1).ToArray();
        var existingEmails = await db.Users
            .Where(user => seedEmails.Contains(user.Email))
            .ToDictionaryAsync(user => user.Email, StringComparer.OrdinalIgnoreCase);

        var changed = false;
        foreach (var seed in seedUsers)
        {
            if (existingEmails.TryGetValue(seed.Item1, out var existing))
            {
                if (!string.Equals(existing.DisplayName, seed.Item2, StringComparison.Ordinal))
                {
                    existing.Update(seed.Item2, null);
                    changed = true;
                }
                continue;
            }

            db.Users.Add(new DrCare.Domain.User(organizationId, seed.Item1, seed.Item2, seed.Item3, hasher.Hash(password)));
            changed = true;
        }

        if (changed)
            await db.SaveChangesAsync();
    }
}
