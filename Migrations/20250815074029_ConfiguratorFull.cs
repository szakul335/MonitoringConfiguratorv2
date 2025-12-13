using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MonitoringConfigurator.Migrations
{
    /// <inheritdoc />
    public partial class ConfiguratorFull : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Configurations");

            migrationBuilder.AddColumn<int>(
                name: "DiskBays",
                table: "Products",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxHddTB",
                table: "Products",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RollLengthM",
                table: "Products",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "StorageTB",
                table: "Products",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SupportsRaid",
                table: "Products",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UpsVA",
                table: "Products",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiskBays",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "MaxHddTB",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "RollLengthM",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "StorageTB",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "SupportsRaid",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "UpsVA",
                table: "Products");

            migrationBuilder.CreateTable(
                name: "Configurations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CameraCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PoePerCameraW = table.Column<int>(type: "int", nullable: false),
                    RequiredResolutionMp = table.Column<double>(type: "float", nullable: false),
                    SelectedCameraId = table.Column<int>(type: "int", nullable: true),
                    SelectedNvrId = table.Column<int>(type: "int", nullable: true),
                    SelectedSwitchId = table.Column<int>(type: "int", nullable: true),
                    UserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Configurations", x => x.Id);
                });
        }
    }
}
