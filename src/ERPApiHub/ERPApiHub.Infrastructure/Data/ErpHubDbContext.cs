using System.Security.Claims;
using ERPApiHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;

namespace ERPApiHub.Infrastructure.Data;

public sealed class ErpHubDbContext(DbContextOptions<ErpHubDbContext> options, IHttpContextAccessor? httpContextAccessor = null) : DbContext(options)
{
    private readonly IHttpContextAccessor? _httpContextAccessor = httpContextAccessor;
    public DbSet<ExternalSystem> ExternalSystems => Set<ExternalSystem>();
    public DbSet<TenantRegistry> TenantRegistries => Set<TenantRegistry>();
    public DbSet<ApiKeyMapping> ApiKeyMappings => Set<ApiKeyMapping>();
    public DbSet<FieldMapping> FieldMappings => Set<FieldMapping>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<WebhookSubscription> WebhookSubscriptions => Set<WebhookSubscription>();
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();
    public DbSet<ErpProcessedEvent> ErpProcessedEvents => Set<ErpProcessedEvent>();

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        ApplyAuditTimestamps();
        return base.SaveChanges();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureTenantRegistry(modelBuilder);
        ConfigureExternalSystems(modelBuilder);
        ConfigureApiKeyMappings(modelBuilder);
        ConfigureFieldMappings(modelBuilder);
        ConfigureAuditLogs(modelBuilder);
        ConfigureWebhookSubscriptions(modelBuilder);
        ConfigureWebhookDeliveries(modelBuilder);
        ConfigureProcessedEvents(modelBuilder);
        SeedBootstrapData(modelBuilder);
    }

    private static void ConfigureTenantRegistry(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TenantRegistry>(entity =>
        {
            entity.ToTable("tenant_registry");
            entity.HasKey(x => x.TenantId);
            entity.Property(x => x.TenantId).HasColumnName("tenant_id").HasMaxLength(26).IsRequired();
            entity.Property(x => x.SiteName).HasColumnName("site_name").HasMaxLength(100).IsRequired();
            entity.Property(x => x.ErpNextHost).HasColumnName("erpnext_host").HasMaxLength(255).IsRequired();
            entity.Property(x => x.HealthStatus).HasColumnName("health_status").HasMaxLength(20).HasDefaultValue("active").IsRequired();
            entity.Property(x => x.LastHealthCheck).HasColumnName("last_health_check").HasColumnType("timestamp with time zone");
            entity.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true).IsRequired();
            MapAuditColumns(entity);
            entity.HasQueryFilter(x => x.DeletedAt == null);
        });
    }

    private static void ConfigureExternalSystems(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ExternalSystem>(entity =>
        {
            entity.ToTable("external_systems");
            entity.HasKey(x => x.SystemId);
            entity.Property(x => x.SystemId).HasColumnName("system_id").HasMaxLength(26).IsRequired();
            entity.Property(x => x.SystemName).HasColumnName("system_name").HasMaxLength(100).IsRequired();
            entity.Property(x => x.SystemType).HasColumnName("system_type").HasMaxLength(50).IsRequired();
            entity.Property(x => x.RateLimitTier).HasColumnName("rate_limit_tier").HasMaxLength(20).HasDefaultValue("standard").IsRequired();
            entity.Property(x => x.TenantId).HasColumnName("tenant_id").HasMaxLength(26).IsRequired();
            entity.Property(x => x.WebhookUrl).HasColumnName("webhook_url").HasMaxLength(500);
            entity.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true).IsRequired();
            MapAuditColumns(entity);
            entity.HasOne(x => x.Tenant)
                .WithMany(x => x.ExternalSystems)
                .HasForeignKey(x => x.TenantId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => x.TenantId).HasDatabaseName("idx_external_systems_tenant");
            entity.HasIndex(x => x.SystemType).HasDatabaseName("idx_external_systems_type");
            entity.HasIndex(x => new { x.SystemName, x.TenantId }).IsUnique().HasDatabaseName("ux_external_systems_name_tenant");
            entity.HasCheckConstraint("ck_external_systems_system_type", "system_type IN ('CRM', 'Ticketing', 'Payment', 'OTA', 'Partner', 'Internal')");
            entity.HasQueryFilter(x => x.DeletedAt == null);
        });
    }

    private static void ConfigureApiKeyMappings(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApiKeyMapping>(entity =>
        {
            entity.ToTable("api_key_mapping");
            entity.HasKey(x => x.MappingId);
            entity.Property(x => x.MappingId).HasColumnName("mapping_id").HasMaxLength(26).IsRequired();
            entity.Property(x => x.SystemId).HasColumnName("system_id").HasMaxLength(26).IsRequired();
            entity.Property(x => x.KeycloakUserId).HasColumnName("keycloak_user_id").HasMaxLength(255).IsRequired();
            entity.Property(x => x.ErpNextApiKeyEnc).HasColumnName("erpnext_api_key_enc").HasColumnType("bytea").IsRequired();
            entity.Property(x => x.ErpNextApiSecretEnc).HasColumnName("erpnext_api_secret_enc").HasColumnType("bytea").IsRequired();
            entity.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true).IsRequired();
            MapAuditColumns(entity);
            entity.HasOne(x => x.ExternalSystem)
                .WithMany(x => x.ApiKeyMappings)
                .HasForeignKey(x => x.SystemId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => x.KeycloakUserId).HasDatabaseName("idx_api_key_mapping_user");
            entity.HasIndex(x => new { x.KeycloakUserId, x.SystemId }).IsUnique().HasDatabaseName("ux_api_key_mapping_user_system");
            entity.HasQueryFilter(x => x.DeletedAt == null);
        });
    }

    private static void ConfigureFieldMappings(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FieldMapping>(entity =>
        {
            entity.ToTable("field_mappings");
            entity.HasKey(x => x.MappingId);
            entity.Property(x => x.MappingId).HasColumnName("mapping_id").HasMaxLength(26).IsRequired();
            entity.Property(x => x.SystemId).HasColumnName("system_id").HasMaxLength(26).IsRequired();
            entity.Property(x => x.ErpNextDoctype).HasColumnName("erpnext_doctype").HasMaxLength(100).IsRequired();
            entity.Property(x => x.ExternalField).HasColumnName("external_field").HasMaxLength(200).IsRequired();
            entity.Property(x => x.ErpNextField).HasColumnName("erpnext_field").HasMaxLength(200).IsRequired();
            entity.Property(x => x.TransformRule).HasColumnName("transform_rule").HasColumnType("text");
            MapAuditColumns(entity);
            entity.HasOne(x => x.ExternalSystem)
                .WithMany(x => x.FieldMappings)
                .HasForeignKey(x => x.SystemId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => x.SystemId).HasDatabaseName("idx_field_mappings_system");
            entity.HasQueryFilter(x => x.DeletedAt == null);
        });
    }

    private static void ConfigureAuditLogs(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("audit_logs");
            entity.HasKey(x => new { x.LogId, x.CreatedAt });
            entity.Property(x => x.LogId).HasColumnName("log_id").HasMaxLength(26).IsRequired();
            entity.Property(x => x.RequestId).HasColumnName("request_id").HasMaxLength(26);
            entity.Property(x => x.TenantId).HasColumnName("tenant_id").HasMaxLength(26).IsRequired();
            entity.Property(x => x.SystemId).HasColumnName("system_id").HasMaxLength(26);
            entity.Property(x => x.UserId).HasColumnName("user_id").HasMaxLength(255);
            entity.Property(x => x.Method).HasColumnName("method").HasMaxLength(10).IsRequired();
            entity.Property(x => x.Endpoint).HasColumnName("endpoint").HasMaxLength(500).IsRequired();
            entity.Property(x => x.StatusCode).HasColumnName("status_code");
            entity.Property(x => x.DurationMs).HasColumnName("duration_ms");
            entity.Property(x => x.RequestSizeBytes).HasColumnName("request_size_bytes");
            entity.Property(x => x.ResponseSizeBytes).HasColumnName("response_size_bytes");
            entity.Property(x => x.ClientIp).HasColumnName("client_ip").HasColumnType("inet");
            entity.Property(x => x.UserAgent).HasColumnName("user_agent").HasColumnType("text");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()").IsRequired();
            entity.Property(x => x.ArchiveStatus).HasColumnName("archive_status").HasMaxLength(20);
            entity.Property(x => x.ArchiveClaimedAt).HasColumnName("archive_claimed_at").HasColumnType("timestamp with time zone");
            entity.HasOne(x => x.ExternalSystem)
                .WithMany(x => x.AuditLogs)
                .HasForeignKey(x => x.SystemId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => x.CreatedAt).HasDatabaseName("idx_audit_logs_created_at");
            entity.HasIndex(x => x.TenantId).HasDatabaseName("idx_audit_logs_tenant");
            entity.HasIndex(x => x.SystemId).HasDatabaseName("idx_audit_logs_system");
            entity.HasIndex(x => x.UserId).HasDatabaseName("idx_audit_logs_user");
        });
    }

    private static void ConfigureWebhookSubscriptions(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WebhookSubscription>(entity =>
        {
            entity.ToTable("webhook_subscriptions");
            entity.HasKey(x => x.SubscriptionId);
            entity.Property(x => x.SubscriptionId).HasColumnName("subscription_id").HasMaxLength(26).IsRequired();
            entity.Property(x => x.SystemId).HasColumnName("system_id").HasMaxLength(26).IsRequired();
            entity.Property(x => x.EventTypes).HasColumnName("event_types").HasColumnType("text[]").IsRequired();
            entity.Property(x => x.WebhookUrl).HasColumnName("webhook_url").HasMaxLength(500).IsRequired();
            entity.Property(x => x.SecretEncrypted).HasColumnName("secret_encrypted").HasColumnType("bytea");
            entity.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true).IsRequired();
            MapAuditColumns(entity);
            entity.HasOne(x => x.ExternalSystem)
                .WithMany(x => x.WebhookSubscriptions)
                .HasForeignKey(x => x.SystemId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => x.SystemId).HasDatabaseName("idx_webhook_sub_system");
            entity.HasQueryFilter(x => x.DeletedAt == null);
        });
    }

    private static void ConfigureWebhookDeliveries(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WebhookDelivery>(entity =>
        {
            entity.ToTable("webhook_deliveries");
            entity.HasKey(x => x.DeliveryId);
            entity.Property(x => x.DeliveryId).HasColumnName("delivery_id").HasMaxLength(26).IsRequired();
            entity.Property(x => x.SubscriptionId).HasColumnName("subscription_id").HasMaxLength(26).IsRequired();
            entity.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
            entity.Property(x => x.HttpStatus).HasColumnName("http_status");
            entity.Property(x => x.ResponseBody).HasColumnName("response_body").HasColumnType("text");
            entity.Property(x => x.AttemptedAt).HasColumnName("attempted_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()").IsRequired();
            entity.Property(x => x.NextRetryAt).HasColumnName("next_retry_at").HasColumnType("timestamp with time zone");
            entity.HasOne(x => x.Subscription)
                .WithMany(x => x.Deliveries)
                .HasForeignKey(x => x.SubscriptionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => x.SubscriptionId).HasDatabaseName("idx_webhook_deliveries_subscription");
        });
    }

    private static void ConfigureProcessedEvents(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ErpProcessedEvent>(entity =>
        {
            entity.ToTable("erp_processed_events");
            entity.HasKey(x => x.ErpProcessedEventId);
            entity.Property(x => x.ErpProcessedEventId).HasColumnName("erp_processed_event_id").HasMaxLength(26).IsRequired();
            entity.Property(x => x.Source).HasColumnName("source").HasMaxLength(100).IsRequired();
            entity.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(100).IsRequired();
            entity.Property(x => x.ProcessedAt).HasColumnName("processed_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()").IsRequired();
            entity.HasIndex(x => new { x.Source, x.EventType }).HasDatabaseName("idx_erp_processed_events_source_type");
        });
    }

    private static void MapAuditColumns<T>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T> entity)
        where T : AuditableEntity
    {
        entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()").IsRequired();
        entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()").IsRequired();
        entity.Property(x => x.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");
        entity.Property(x => x.CreatedBy).HasColumnName("created_by").HasMaxLength(255);
        entity.Property(x => x.UpdatedBy).HasColumnName("updated_by").HasMaxLength(255);
    }

    private static void SeedBootstrapData(ModelBuilder modelBuilder)
    {
        var createdAt = new DateTimeOffset(2026, 5, 27, 0, 0, 0, TimeSpan.Zero);
        modelBuilder.Entity<TenantRegistry>().HasData(new TenantRegistry
        {
            TenantId = "01J00000000000000000000000",
            SiteName = "frontend",
            ErpNextHost = "erpnext-frontend:8080",
            HealthStatus = "active",
            LastHealthCheck = null,
            IsActive = true,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
            CreatedBy = "system",
            UpdatedBy = "system"
        });

        modelBuilder.Entity<ExternalSystem>().HasData(new ExternalSystem
        {
            SystemId = "01J00000000000000000000001",
            SystemName = "bootstrap-internal",
            SystemType = ExternalSystemTypes.Internal,
            RateLimitTier = "standard",
            TenantId = "01J00000000000000000000000",
            IsActive = true,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
            CreatedBy = "system",
            UpdatedBy = "system"
        });
    }

    private void ApplyAuditTimestamps()
    {
        var now = DateTimeOffset.UtcNow;
        var userId = _httpContextAccessor?.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
                if (userId is not null && entry.Entity.CreatedBy is null)
                    entry.Entity.CreatedBy = userId;
                if (userId is not null && entry.Entity.UpdatedBy is null)
                    entry.Entity.UpdatedBy = userId;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                if (userId is not null)
                    entry.Entity.UpdatedBy = userId;
            }
            else if (entry.State == EntityState.Deleted)
            {
                entry.State = EntityState.Modified;
                entry.Entity.DeletedAt = now;
                entry.Entity.UpdatedAt = now;
                if (userId is not null)
                    entry.Entity.UpdatedBy = userId;
            }
        }
    }
}
