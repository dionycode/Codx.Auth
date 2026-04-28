using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Codx.Auth.Data.Migrations.Users
{
    /// <inheritdoc />
    public partial class AddIsDefaultToApplicationRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDefault",
                table: "EnterpriseApplicationRoles",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsDefault",
                table: "EnterpriseApplicationRoles");
        }
    }
}
