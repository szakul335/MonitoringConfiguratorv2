using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MonitoringConfigurator.Migrations
{
    /// <inheritdoc />
    public partial class AddLensToProducts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Lens",
                table: "Products",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Lens",
                table: "Products");
        }
    }
}
