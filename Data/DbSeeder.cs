using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace MonitoringConfigurator.Data
{
    public static class DbSeeder
    {
        public static async Task SeedAsync(IServiceProvider services)
        {
            using var scope = services.CreateScope();
            var sp = scope.ServiceProvider;

            var ctx = sp.GetRequiredService<AppDbContext>();

            // Aplikowanie migracji lub utworzenie bazy, jeśli nie istnieje
            try
            {
                await ctx.Database.MigrateAsync();
            }
            catch
            {
                await ctx.Database.EnsureCreatedAsync();
            }

            await ctx.Database.ExecuteSqlRawAsync(@"
                IF NOT EXISTS (
                    SELECT 1 FROM sys.columns
                    WHERE Name = 'ShortDescription' AND Object_ID = OBJECT_ID('Products')
                )
                BEGIN
                    ALTER TABLE [Products] ADD [ShortDescription] NVARCHAR(300) NULL;
                END


                IF NOT EXISTS (


                IF NOT EXISTS (

            ");

            // Usuń starą kolumnę Price, jeśli wciąż istnieje
            await ctx.Database.ExecuteSqlRawAsync(@"
                IF EXISTS (


                    SELECT 1 FROM sys.columns
                    WHERE Name = 'Price' AND Object_ID = OBJECT_ID('Products')
                )
                BEGIN

                    ALTER TABLE [Products] ADD [Price] DECIMAL(18, 2) NOT NULL CONSTRAINT DF_Products_Price DEFAULT 0;
                    ALTER TABLE [Products] DROP CONSTRAINT DF_Products_Price;


                    ALTER TABLE [Products] ADD [Price] DECIMAL(18, 2) NOT NULL CONSTRAINT DF_Products_Price DEFAULT 0;
                    ALTER TABLE [Products] DROP CONSTRAINT DF_Products_Price;

                    ALTER TABLE [Products] DROP COLUMN [Price];


                END
            ");

            // Seedowanie użytkownika Admin (tylko jeśli nie istnieje)
            var um = sp.GetRequiredService<UserManager<IdentityUser>>();
            var adminEmail = "admin@demo.pl";
            var adminPass = "Admin123!";

            if (await um.FindByEmailAsync(adminEmail) is null)
            {
                var user = new IdentityUser
                {
                    UserName = adminEmail,
                    Email = adminEmail,
                    EmailConfirmed = true
                };
                await um.CreateAsync(user, adminPass);
            }

            // Sekcja seedowania produktów została usunięta.
        }
    }
}
