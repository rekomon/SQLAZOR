using Microsoft.EntityFrameworkCore;
using System.Data;
using Microsoft.Data.SqlClient;
using Shop.Data;
using Shop.Data.Components;
using Shop.Data.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddControllers();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found - check appsettings.json.");

builder.Services.AddDbContext<ShopDbContext>(options => options.UseSqlServer(connectionString));

// Dapper needs a raw IDbConnection, registered separately from the DbContext above -
// the generated {Table}Service classes (and the dashboard stats) use Dapper directly, not EF Core.
builder.Services.AddScoped<IDbConnection>(_ => new SqlConnection(connectionString));
builder.Services.AddScoped<DashboardStatsService>();
builder.Services.AddGeneratedCrudServices();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();

app.MapControllers();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
