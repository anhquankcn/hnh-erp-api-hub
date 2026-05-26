using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERPApiHub.Infrastructure.Migrations;

/// <inheritdoc />
public partial class InitialErpHubSchema : Migration
{
    /// <inheritdoc />
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
        modelBuilder
            .HasAnnotation("ProductVersion", "9.0.5")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);

        // Snapshot matches the full model defined in ErpHubDbContext.OnModelCreating.
        // Key annotations: audit_logs partitioned by created_at, soft-delete query filters.
#pragma warning restore 612, 618
    }

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "tenant_registry",
            columns: table => new
            {
                tenant_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                site_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                erpnext_host = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                health_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "active"),
                is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_by = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: true),
                updated_by = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_tenant_registry", x => x.tenant_id);
            });

        migrationBuilder.CreateTable(
            name: "external_systems",
            columns: table => new
            {
                system_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                system_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                system_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                rate_limit_tier = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "standard"),
                tenant_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                webhook_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_by = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: true),
                updated_by = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_external_systems", x => x.system_id);
                table.CheckConstraint("ck_external_systems_system_type", "system_type IN ('CRM', 'Ticketing', 'Payment', 'OTA', 'Partner', 'Internal')");
                table.ForeignKey(
                    name: "fk_external_systems_tenant_registry_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenant_registry",
                    principalColumn: "tenant_id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "api_key_mapping",
            columns: table => new
            {
                mapping_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                system_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                keycloak_user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                erpnext_api_key_enc = table.Column<byte[]>(type: "bytea", nullable: false),
                erpnext_api_secret_enc = table.Column<byte[]>(type: "bytea", nullable: false),
                is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_by = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: true),
                updated_by = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_api_key_mapping", x => x.mapping_id);
                table.ForeignKey(
                    name: "fk_api_key_mapping_external_systems_system_id",
                    column: x => x.system_id,
                    principalTable: "external_systems",
                    principalColumn: "system_id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "field_mappings",
            columns: table => new
            {
                mapping_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                system_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                erpnext_doctype = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                external_field = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                erpnext_field = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                transform_rule = table.Column<string>(type: "text", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_by = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: true),
                updated_by = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_field_mappings", x => x.mapping_id);
                table.ForeignKey(
                    name: "fk_field_mappings_external_systems_system_id",
                    column: x => x.system_id,
                    principalTable: "external_systems",
                    principalColumn: "system_id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "webhook_subscriptions",
            columns: table => new
            {
                subscription_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                system_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                event_types = table.Column<string[]>(type: "text[]", nullable: false),
                webhook_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                secret_encrypted = table.Column<byte[]>(type: "bytea", nullable: true),
                is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_by = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: true),
                updated_by = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_webhook_subscriptions", x => x.subscription_id);
                table.ForeignKey(
                    name: "fk_webhook_subscriptions_external_systems_system_id",
                    column: x => x.system_id,
                    principalTable: "external_systems",
                    principalColumn: "system_id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "webhook_deliveries",
            columns: table => new
            {
                delivery_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                subscription_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                http_status = table.Column<int>(type: "integer", nullable: true),
                response_body = table.Column<string>(type: "text", nullable: true),
                attempted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                next_retry_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_webhook_deliveries", x => x.delivery_id);
                table.ForeignKey(
                    name: "fk_webhook_deliveries_webhook_subscriptions_subscription_id",
                    column: x => x.subscription_id,
                    principalTable: "webhook_subscriptions",
                    principalColumn: "subscription_id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "erp_processed_events",
            columns: table => new
            {
                erp_processed_event_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_erp_processed_events", x => x.erp_processed_event_id);
            });

        migrationBuilder.Sql("""
            CREATE TABLE audit_logs (
                log_id VARCHAR(26) NOT NULL,
                request_id VARCHAR(26),
                tenant_id VARCHAR(26) NOT NULL,
                system_id VARCHAR(26) REFERENCES external_systems(system_id),
                user_id VARCHAR(255),
                method VARCHAR(10) NOT NULL,
                endpoint VARCHAR(500) NOT NULL,
                status_code INTEGER,
                duration_ms INTEGER,
                request_size_bytes INTEGER,
                response_size_bytes INTEGER,
                client_ip INET,
                user_agent TEXT,
                created_at TIMESTAMPTZ DEFAULT NOW() NOT NULL,
                PRIMARY KEY (log_id, created_at)
            ) PARTITION BY RANGE (created_at);
            """);

        migrationBuilder.Sql("""
            CREATE TABLE audit_logs_2026_05 PARTITION OF audit_logs
                FOR VALUES FROM ('2026-05-01') TO ('2026-06-01');

            CREATE TABLE audit_logs_2026_06 PARTITION OF audit_logs
                FOR VALUES FROM ('2026-06-01') TO ('2026-07-01');

            CREATE TABLE audit_logs_default PARTITION OF audit_logs DEFAULT;
            """);

        migrationBuilder.CreateIndex("idx_external_systems_tenant", "external_systems", "tenant_id");
        migrationBuilder.CreateIndex("idx_external_systems_type", "external_systems", "system_type");
        migrationBuilder.CreateIndex("ux_external_systems_name_tenant", "external_systems", new[] { "system_name", "tenant_id" }, unique: true);
        migrationBuilder.CreateIndex("idx_api_key_mapping_user", "api_key_mapping", "keycloak_user_id");
        migrationBuilder.CreateIndex("ux_api_key_mapping_user_system", "api_key_mapping", new[] { "keycloak_user_id", "system_id" }, unique: true);
        migrationBuilder.CreateIndex("ix_api_key_mapping_system_id", "api_key_mapping", "system_id");
        migrationBuilder.CreateIndex("idx_field_mappings_system", "field_mappings", "system_id");
        migrationBuilder.CreateIndex("idx_webhook_sub_system", "webhook_subscriptions", "system_id");
        migrationBuilder.CreateIndex("idx_webhook_deliveries_subscription", "webhook_deliveries", "subscription_id");
        migrationBuilder.CreateIndex("idx_erp_processed_events_source_type", "erp_processed_events", new[] { "source", "event_type" });

        migrationBuilder.Sql("""
            CREATE INDEX idx_audit_logs_created_at ON audit_logs(created_at);
            CREATE INDEX idx_audit_logs_tenant ON audit_logs(tenant_id);
            CREATE INDEX idx_audit_logs_system ON audit_logs(system_id);
            CREATE INDEX idx_audit_logs_user ON audit_logs(user_id);
            """);

        migrationBuilder.InsertData(
            table: "tenant_registry",
            columns: new[] { "tenant_id", "site_name", "erpnext_host", "health_status", "is_active", "created_at", "updated_at", "created_by", "updated_by" },
            values: new object[] { "01J00000000000000000000000", "frontend", "erpnext-frontend:8080", "active", true, new DateTimeOffset(2026, 5, 27, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 5, 27, 0, 0, 0, TimeSpan.Zero), "system", "system" });

        migrationBuilder.InsertData(
            table: "external_systems",
            columns: new[] { "system_id", "system_name", "system_type", "rate_limit_tier", "tenant_id", "is_active", "created_at", "updated_at", "created_by", "updated_by" },
            values: new object[] { "01J00000000000000000000001", "bootstrap-internal", "Internal", "standard", "01J00000000000000000000000", true, new DateTimeOffset(2026, 5, 27, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 5, 27, 0, 0, 0, TimeSpan.Zero), "system", "system" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS audit_logs CASCADE;");
        migrationBuilder.DropTable(name: "webhook_deliveries");
        migrationBuilder.DropTable(name: "api_key_mapping");
        migrationBuilder.DropTable(name: "field_mappings");
        migrationBuilder.DropTable(name: "erp_processed_events");
        migrationBuilder.DropTable(name: "webhook_subscriptions");
        migrationBuilder.DropTable(name: "external_systems");
        migrationBuilder.DropTable(name: "tenant_registry");
    }
}
