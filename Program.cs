using Microsoft.EntityFrameworkCore;
using SmartELibrary.Data;
using SmartELibrary.Filters;
using SmartELibrary.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        var host = builder.Configuration["Database:Host"] ?? "localhost";
        var port = builder.Configuration["Database:Port"] ?? "3306";
        var name = builder.Configuration["Database:Name"] ?? "smartelibrary_db";
        var user = builder.Configuration["Database:User"] ?? "root";
        var password = builder.Configuration["Database:Password"] ?? string.Empty;
        connectionString = $"server={host};port={port};database={name};user={user};password={password};";
    }

    options.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 36)));
});

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IOtpService, OtpService>();
builder.Services.AddScoped<IProgressService, ProgressService>();
builder.Services.AddScoped<ITeacherCodeGenerator, TeacherCodeGenerator>();
builder.Services.AddScoped<IStudentSemesterApprovalService, StudentSemesterApprovalService>();
builder.Services.AddScoped<StudentSemesterApprovalFilter>();
builder.Services.AddSingleton<IStudentSessionTracker, StudentSessionTracker>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var logger = scope.ServiceProvider
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("DatabaseStartup");
    try
    {
        db.Database.Migrate();

        var wipeUserGenerated = args.Any(a => string.Equals(a, "--wipe-user-generated", StringComparison.OrdinalIgnoreCase));
        var wipeAllData = args.Any(a => string.Equals(a, "--wipe-all-data", StringComparison.OrdinalIgnoreCase));
        var noSeed = args.Any(a => string.Equals(a, "--no-seed", StringComparison.OrdinalIgnoreCase));

        logger.LogInformation("Connected database: {Database}", db.Database.GetDbConnection().Database);
        logger.LogInformation("Wipe flags: userGenerated={WipeUserGenerated}, allData={WipeAllData}, noSeed={NoSeed}", wipeUserGenerated, wipeAllData, noSeed);

        if (wipeAllData)
        {
            logger.LogWarning("Wiping ALL application tables...");

            var usersBefore = await db.Users.CountAsync();
            logger.LogWarning("Before wipe: Users={Users}", usersBefore);

            await DatabaseWipeService.WipeAllApplicationDataAsync(db);

            var usersAfter = await db.Users.CountAsync();
            logger.LogWarning("After wipe: Users={Users}", usersAfter);
        }
        else if (wipeUserGenerated)
        {
            logger.LogWarning("Wiping user-generated content tables...");

            var materialsBefore = await db.Materials.CountAsync();
            var pagesBefore = await db.MaterialPages.CountAsync();
            logger.LogWarning("Before wipe: Materials={Materials}, MaterialPages={Pages}", materialsBefore, pagesBefore);

            await DatabaseWipeService.WipeUserGeneratedContentAsync(db);

            var materialsAfter = await db.Materials.CountAsync();
            var pagesAfter = await db.MaterialPages.CountAsync();
            logger.LogWarning("After wipe: Materials={Materials}, MaterialPages={Pages}", materialsAfter, pagesAfter);
        }

        if ((wipeUserGenerated || wipeAllData) && noSeed)
        {
            logger.LogInformation("Wipe completed with --no-seed. Exiting by request.");
            return;
        }

        var adminPhone = app.Configuration["AdminSeed:PhoneNumber"];
        var adminPassword = app.Configuration["AdminSeed:Password"];
        var adminName = app.Configuration["AdminSeed:FullName"] ?? "System Admin";
        await AdminSeedService.EnsureAdminExistsAsync(db, adminPhone ?? string.Empty, adminPassword ?? string.Empty, adminName);

        await RoleTableBackfillService.BackfillAsync(db);

        if (wipeUserGenerated || wipeAllData)
        {
            logger.LogInformation("Wipe completed successfully. Exiting by request.");
            return;
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Database migration/seed/backfill failed.");
        throw;
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PORT")))
{
    app.UseHttpsRedirection();
}
app.UseStaticFiles();
app.UseRouting();
app.UseSession();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run($"http://0.0.0.0:{port}");