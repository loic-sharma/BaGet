var builder = WebApplication.CreateBuilder(args);

builder.Services.AddBaGetWebApplication(app =>
{
    // Use SQLite as BaGet's database and store packages on the local file system.
    app.AddSqliteDatabase();
    app.AddFileStorage();
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseStaticFiles();
app.UseRouting();

app.UseEndpoints(endpoints =>
{
    // Add BaGet's endpoints.
    new BaGetEndpointBuilder().MapEndpoints(endpoints);
});

await app.RunMigrationsAsync();

await app.RunAsync();