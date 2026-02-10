using Microsoft.AspNetCore.Identity;
using SkinAI.API.Models;

namespace SkinAI.API.Data
{
    public static class DataSeeder
    {
        public static async Task SeedAsync(IServiceProvider services)
        {
            using var scope = services.CreateScope();

            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<int>>>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

            // 1) Seed Roles
            string[] roles = { "Admin", "Doctor", "Patient" };

            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new IdentityRole<int>(role));
                }
            }

            // 2) Seed Admin User
            var adminEmail = "admin@skinai.com";
            var admin = await userManager.FindByEmailAsync(adminEmail);

            if (admin == null)
            {
                admin = new User
                {
                    UserName = adminEmail,
                    Email = adminEmail,
                    FullName = "Admin",
                   
                    Role = "Admin",          // لو عندك property اسمها Role
                    IsApproved = true,
                    EmailConfirmed = true
                };

                var createResult = await userManager.CreateAsync(admin, "Admin@12345");

                if (!createResult.Succeeded)
                {
                    var errors = string.Join(", ", createResult.Errors.Select(e => e.Description));
                    throw new Exception("Admin seed failed: " + errors);
                }
            }
            else
            {
                // ✅ لو الأدمن موجود من قبل: ثبتي خصائصه
                if (admin.Role != "Admin")
                {
                    admin.Role = "Admin";
                    await userManager.UpdateAsync(admin);
                }

                if (!admin.EmailConfirmed)
                {
                    admin.EmailConfirmed = true;
                    await userManager.UpdateAsync(admin);
                }

                if (!admin.IsApproved)
                {
                    admin.IsApproved = true;
                    await userManager.UpdateAsync(admin);
                }
            }

            // ✅ تأكدي إن admin واخد Role "Admin" في Identity Roles
            if (!await userManager.IsInRoleAsync(admin, "Admin"))
            {
                await userManager.AddToRoleAsync(admin, "Admin");
            }
        }
    }
}
