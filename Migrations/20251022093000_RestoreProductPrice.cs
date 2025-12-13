using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MonitoringConfigurator.Migrations
{
    /// <inheritdoc />
    public partial class RestoreProductPrice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF NOT EXISTS (
                    SELECT 1 FROM sys.columns
                    WHERE Name = 'Price' AND Object_ID = OBJECT_ID('Products')
                )
                BEGIN
                    ALTER TABLE [Products] ADD [Price] DECIMAL(18, 2) NOT NULL CONSTRAINT DF_Products_Price DEFAULT 0;
                    ALTER TABLE [Products] DROP CONSTRAINT DF_Products_Price;
                END
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF EXISTS (
                    SELECT 1 FROM sys.columns
                    WHERE Name = 'Price' AND Object_ID = OBJECT_ID('Products')
                )
                BEGIN
                    ALTER TABLE [Products] DROP COLUMN [Price];
                END
            ");
        }
    }
}
