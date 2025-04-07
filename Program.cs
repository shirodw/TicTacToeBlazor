using TicTacToeBlazor.Components;
using Microsoft.EntityFrameworkCore;
using TicTacToeBlazor.Data;
using TicTacToeBlazor.Services;
using TicTacToeBlazor.Hubs;
namespace TicTacToeBlazor;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddSingleton<GameStateService>();

        // Add services to the container.
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents(); // Ensure Server interactivity is added

        // Add DbContext
        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
        builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseSqlite(connectionString));
        // Use AddDbContextFactory for Blazor Server to handle context lifetime correctly
        builder.Services.AddScoped(p => p.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext());

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }

        app.UseHttpsRedirection();

        app.UseAntiforgery();

        app.MapStaticAssets();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();
        app.MapHub<GameHub>("/gamehub"); // Define the URL for the hub
        app.Run();
    }
}
