using Microsoft.EntityFrameworkCore; // Make sure this is present
using TicTacToeBlazor.Data;
using TicTacToeBlazor.Hubs;
using TicTacToeBlazor.Services;
// using TicTacToeBlazor.Components; // Usually not needed directly here unless App is elsewhere

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// --- Database Configuration ---
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
// Use AddDbContextFactory for Blazor Server to manage DbContext lifetime correctly
builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));
// Register a scoped DbContext for simpler injection patterns when needed
builder.Services.AddScoped(p =>
    p.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext());

// --- Application Services ---
builder.Services.AddSingleton<GameStateService>(); // Manages active games and players

// --- Blazor Server Configuration ---
// Configure detailed errors in development
builder.Services.AddServerSideBlazor()
    .AddCircuitOptions(options =>
    {
        if (builder.Environment.IsDevelopment())
        {
            options.DetailedErrors = true;
        }
        // You could configure other circuit options here if needed
        // options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(3);
    });


var app = builder.Build();

// --- Apply Database Migrations On Startup ---
// Create a scope to resolve services like the DbContextFactory
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>(); // Get logger for logging migration errors
    try
    {
        logger.LogInformation("Attempting to apply database migrations...");
        var dbContextFactory = services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        // Create a DbContext instance within this scope
        using (var dbContext = dbContextFactory.CreateDbContext())
        {
            // Apply any pending migrations to the database
            // This will create the database and tables if they don't exist
            await dbContext.Database.MigrateAsync();
            logger.LogInformation("Database migrations applied successfully (or were up-to-date).");
        }
    }
    catch (Exception ex)
    {
        // Log error if migrations fail
        logger.LogError(ex, "An error occurred while applying database migrations.");
        // Depending on the severity, you might want to stop the application
        // For now, we log and continue, but the app might not function correctly without the DB schema.
        // throw; // Uncomment to stop the app if migrations are critical
    }
}
// --- End Apply Database Migrations ---


// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

//app.UseHttpsRedirection(); // Often REMOVED when behind a reverse proxy (like Railway) that handles HTTPS termination. Test if needed.

app.UseStaticFiles(); // Serve static files from wwwroot

// Antiforgery is important for Blazor Server security
app.UseAntiforgery();

// Map Blazor components and enable Server interactivity
app.MapRazorComponents<TicTacToeBlazor.Components.App>()
    .AddInteractiveServerRenderMode();

// Map the SignalR Hub endpoint
app.MapHub<GameHub>("/gamehub");

// Start the application
app.Run();
