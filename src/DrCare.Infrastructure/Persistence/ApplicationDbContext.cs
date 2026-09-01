using DrCare.Domain;
using Microsoft.EntityFrameworkCore;

namespace DrCare.Infrastructure.Persistence;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<LocationAnalysis> LocationAnalyses => Set<LocationAnalysis>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();
    public DbSet<FollowUpTask> FollowUpTasks => Set<FollowUpTask>();
    public DbSet<User> Users => Set<User>();
    public DbSet<WorkflowRecord> WorkflowRecords => Set<WorkflowRecord>();
    public DbSet<DownPayment> DownPayments => Set<DownPayment>();
    public DbSet<LeadDocument> LeadDocuments => Set<LeadDocument>();
    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<ContractSigningRequest> ContractSigningRequests => Set<ContractSigningRequest>();
    public DbSet<Endorsement> Endorsements => Set<Endorsement>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<PreLaunchChecklist> PreLaunchChecklists => Set<PreLaunchChecklist>();
    public DbSet<PreLaunchItem> PreLaunchItems => Set<PreLaunchItem>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<EmailOutboxMessage> EmailOutboxMessages => Set<EmailOutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Email).IsUnique();
            entity.Property(x => x.Email).HasMaxLength(254).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(160).IsRequired();
            entity.Property(x => x.PasswordHash).HasMaxLength(512).IsRequired();
            entity.Property(x => x.Role).HasConversion<string>().HasMaxLength(40);
        });

        modelBuilder.Entity<WorkflowRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.OrganizationId, x.Operation, x.CreatedAt });
            entity.HasIndex(x => new { x.OrganizationId, x.LeadId, x.CreatedAt });
            entity.Property(x => x.Operation).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(40).IsRequired();
            entity.Property(x => x.PayloadJson).HasColumnType("jsonb").IsRequired();
        });

        modelBuilder.Entity<DownPayment>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.OrganizationId, x.LeadId }).IsUnique();
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            entity.Property(x => x.InvoiceNumber).HasMaxLength(80);
            entity.Property(x => x.ConfirmationReference).HasMaxLength(120);
            entity.Property(x => x.SubmittedBy);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(40);
        });

        modelBuilder.Entity<LeadDocument>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.OrganizationId, x.LeadId, x.DocumentType });
            entity.Property(x => x.DocumentType).HasMaxLength(60).IsRequired();
            entity.Property(x => x.FileName).HasMaxLength(160).IsRequired();
            entity.Property(x => x.ContentType).HasMaxLength(120).IsRequired();
            entity.Property(x => x.ObjectKey).HasMaxLength(512).IsRequired();
            entity.Property(x => x.Sha256).HasMaxLength(128);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(40);
        });

        modelBuilder.Entity<Contract>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.OrganizationId, x.LeadId }).IsUnique();
            entity.Property(x => x.TemplateCode).HasMaxLength(80).IsRequired();
            entity.Property(x => x.TemplateVersion).HasMaxLength(32).IsRequired();
            entity.Property(x => x.FranchiseeSignerName).HasMaxLength(160);
            entity.Property(x => x.DrCareSignerName).HasMaxLength(160);
            entity.Property(x => x.RevisionReason).HasMaxLength(4000);
            entity.Property(x => x.RevisionRequestedByName).HasMaxLength(160);
            entity.Property(x => x.ReviewChecklistJson).HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(40);
            entity.Property(x => x.RenderedHtml).HasColumnType("text");
            entity.Property(x => x.CurrentPdfObjectKey).HasMaxLength(512);
            entity.Property(x => x.SignedPdfObjectKey).HasMaxLength(512);
            entity.Property(x => x.SignedPdfSha256).HasMaxLength(64);
        });

        modelBuilder.Entity<ContractSigningRequest>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => new { x.OrganizationId, x.ContractId, x.Status });
            entity.Property(x => x.SignerRole).HasMaxLength(40).IsRequired();
            entity.Property(x => x.SignerName).HasMaxLength(160).IsRequired();
            entity.Property(x => x.SignerEmail).HasMaxLength(254).IsRequired();
            entity.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(40);
            entity.Property(x => x.SignatureSha256).HasMaxLength(64);
            entity.Property(x => x.SignedDocumentSha256).HasMaxLength(64);
            entity.Property(x => x.IpAddress).HasMaxLength(64);
            entity.Property(x => x.UserAgent).HasMaxLength(500);
        });

        modelBuilder.Entity<Endorsement>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.OrganizationId, x.LeadId });
            entity.Property(x => x.ReceivingTeam).HasMaxLength(160).IsRequired();
            entity.Property(x => x.HandoffNotes).HasMaxLength(4000).IsRequired();
            entity.Property(x => x.BoundaryJson).HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(40);
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.OrganizationId, x.RecipientId, x.Read, x.CreatedAt });
            entity.Property(x => x.Type).HasMaxLength(60).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Message).HasMaxLength(2000).IsRequired();
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        });
        modelBuilder.Entity<PasswordResetToken>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        });

        modelBuilder.Entity<PreLaunchChecklist>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.OrganizationId, x.LeadId }).IsUnique();
            entity.Property(x => x.ProductLine).HasConversion<string>().HasMaxLength(40);
            entity.Property(x => x.Status).HasMaxLength(40).IsRequired();
            entity.HasMany<PreLaunchItem>("_items").WithOne().HasForeignKey(x => x.ChecklistId).OnDelete(DeleteBehavior.Cascade);
            entity.Ignore(x => x.Items);
        });
        modelBuilder.Entity<PreLaunchItem>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ChecklistId, x.Code }).IsUnique();
            entity.Property(x => x.Code).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Notes).HasMaxLength(2000);
        });
        modelBuilder.Entity<AppSetting>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.OrganizationId, x.Key }).IsUnique();
            entity.Property(x => x.Key).HasMaxLength(120).IsRequired();
            entity.Property(x => x.ValueJson).HasColumnType("jsonb").IsRequired();
        });
        modelBuilder.Entity<EmailOutboxMessage>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.Status, x.AvailableAt, x.CreatedAt });
            entity.HasIndex(x => new { x.OrganizationId, x.IdempotencyKey }).IsUnique();
            entity.Property(x => x.RecipientEmail).HasMaxLength(254).IsRequired();
            entity.Property(x => x.RecipientName).HasMaxLength(160);
            entity.Property(x => x.Subject).HasMaxLength(300).IsRequired();
            entity.Property(x => x.HtmlBody).HasColumnType("text").IsRequired();
            entity.Property(x => x.TextBody).HasColumnType("text");
            entity.Property(x => x.Category).HasMaxLength(80).IsRequired();
            entity.Property(x => x.AttachmentsJson).HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.IdempotencyKey).HasMaxLength(200);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(40);
            entity.Property(x => x.LastError).HasMaxLength(2000);
        });

        modelBuilder.Entity<Lead>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.OrganizationId, x.State });
            entity.HasIndex(x => new { x.OrganizationId, x.AssignedAgentId, x.State });
            entity.Property(x => x.FullName).HasMaxLength(160).IsRequired();
            entity.Property(x => x.ContactNumber).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(254).IsRequired();
            entity.Property(x => x.SourceOfIncome).HasMaxLength(160).IsRequired();
            entity.Property(x => x.LeadSource).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Address).HasMaxLength(500);
            entity.Property(x => x.Industry).HasMaxLength(160);
            entity.Property(x => x.QuestionsConcerns).HasMaxLength(4000);
            entity.Property(x => x.WelcomeEmailReceived).HasMaxLength(20);
            entity.Property(x => x.GoodTimeToDiscuss).HasMaxLength(40);
            entity.Property(x => x.LastCallOutcome).HasMaxLength(80);
            entity.Property(x => x.PreferredLocation).HasMaxLength(240);
            entity.Property(x => x.State).HasConversion<string>().HasMaxLength(40);
            entity.Property(x => x.ProductLine).HasConversion<string>().HasMaxLength(40);
            entity.Property(x => x.ListPrice).HasPrecision(18, 2);
            entity.Property(x => x.ActualPrice).HasPrecision(18, 2);
            entity.Ignore(x => x.Activities);
            entity.Ignore(x => x.Tasks);
            entity.HasMany<ActivityLog>("_activities").WithOne().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany<FollowUpTask>("_tasks").WithOne().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.LocationAnalysis).WithOne().HasForeignKey<LocationAnalysis>(x => x.LeadId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LocationAnalysis>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.LeadId).IsUnique();
            entity.Property(x => x.PreferredLocation).HasMaxLength(240).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(40).IsRequired();
            entity.Property(x => x.LeaseOwnershipStatus).HasMaxLength(40).IsRequired();
            entity.Property(x => x.AssessmentJson).HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.Notes).HasMaxLength(2000);
            entity.Property(x => x.ReviewNotes).HasMaxLength(2000);
            entity.Property(x => x.RevisionReason).HasMaxLength(2000);
        });

        modelBuilder.Entity<ActivityLog>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.OrganizationId, x.LeadId, x.CreatedAt });
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(40);
            entity.Property(x => x.Message).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.FromState).HasConversion<string>().HasMaxLength(40);
            entity.Property(x => x.ToState).HasConversion<string>().HasMaxLength(40);
        });

        modelBuilder.Entity<FollowUpTask>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.OrganizationId, x.AssignedTo, x.Status });
            entity.Property(x => x.Title).HasMaxLength(240).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(40);
        });
    }
}
