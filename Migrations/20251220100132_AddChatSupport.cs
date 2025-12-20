using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MonitoringConfigurator.Migrations
{
    /// <inheritdoc />
    public partial class AddChatSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ParentId",
                table: "Contacts",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_ParentId",
                table: "Contacts",
                column: "ParentId");

            migrationBuilder.AddForeignKey(
                name: "FK_Contacts_Contacts_ParentId",
                table: "Contacts",
                column: "ParentId",
                principalTable: "Contacts",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Contacts_Contacts_ParentId",
                table: "Contacts");

            migrationBuilder.DropIndex(
                name: "IX_Contacts_ParentId",
                table: "Contacts");

            migrationBuilder.DropColumn(
                name: "ParentId",
                table: "Contacts");
        }
    }
}
