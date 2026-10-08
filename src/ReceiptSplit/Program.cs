using System.Text.Json.Serialization;
using ReceiptSplit.Accounts;
using ReceiptSplit.Accounts.CloudflareAccess;
using ReceiptSplit.Data;
using ReceiptSplit.Extraction;
using ReceiptSplit.Receipts;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Logs go to the console only: in Docker the container runtime collects stdout. Levels come from the Serilog
// section of the configuration. The static Log.Logger is left alone because nothing uses it, and test hosts
// running side by side would otherwise replace each other's.
builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"),
    preserveStaticLogger: true);

builder.AddDatabase();
builder.AddExtraction();
builder.AddCloudflareAccess();
builder.AddReceipts();

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// The built React app (receiptsplit.client) is published into wwwroot. The page and its files hold no data, and
// load without an account so a refused sign-in can still be explained.
app.UseDefaultFiles();
app.MapStaticAssets().AllowAnonymous();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseAccounts();
app.UseAuthorization();

app.MapControllers();

// Unknown API paths stay 404; every other unmatched path is a client-side route of the React app.
app.MapFallback("/api/{**path}", () => Results.NotFound());
app.MapFallbackToFile("/index.html").AllowAnonymous();

// Before anything starts, so settings or a model server that can't be used stop a deploy instead of failing every
// request or receipt.
if (!await app.Services.CheckAccountSettingsAsync()
    || !await app.Services.CheckCloudflareAccessAsync()
    || !await app.Services.CheckModelServerAsync())
{
    return 1;
}

app.Run();
return 0;
