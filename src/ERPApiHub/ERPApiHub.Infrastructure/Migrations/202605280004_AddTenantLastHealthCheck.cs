using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERPApiHub.Infrastructure.Migrations;

public partial class AddTenantLastHealthCheck : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "last_health_check",
            table: "tenant_registry",
            type: "timestamp with time zone",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "last_health_check",
            table: "tenant_registry");
    }
}
