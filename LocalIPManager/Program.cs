using LocalIPManager.Components;
using LocalIPManager.Data;
using LocalIPManager.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
	.AddInteractiveServerComponents();

var connectionString =
	builder.Configuration.GetConnectionString("DefaultConnection")
		?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContextFactory<DeviceDbContext>(options =>
{
	options.UseSqlite(connectionString);
});

builder.Services.AddScoped<DeviceService>();
builder.Services.AddScoped<DeviceImportExportService>();
builder.Services.AddScoped<NetworkScannerService>();
builder.Services.AddScoped<NotificationService>();

var app = builder.Build();

// Create the local database and apply pending migrations before serving requests.
await using (var scope = app.Services.CreateAsyncScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<DeviceDbContext>>();
    await using var db = await factory.CreateDbContextAsync();
    await db.Database.MigrateAsync();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
	app.UseExceptionHandler("/Error", createScopeForErrors: true);
	// The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
	app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
	.AddInteractiveServerRenderMode();

app.Run();
