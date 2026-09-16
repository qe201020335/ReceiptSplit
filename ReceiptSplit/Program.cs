using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ReceiptSplit.Data;
using ReceiptSplit.Extraction;
using ReceiptSplit.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<StorageOptions>()
    .Bind(builder.Configuration.GetSection(StorageOptions.SectionName))
    .PostConfigure(o => o.Root = Path.GetFullPath(o.Root, builder.Environment.ContentRootPath));
builder.Services.AddOptions<LlmOptions>()
    .Bind(builder.Configuration.GetSection(LlmOptions.SectionName));

builder.Services.AddDbContext<AppDbContext>((sp, options) =>
    options.UseSqlite($"Data Source={sp.GetRequiredService<IOptions<StorageOptions>>().Value.DatabasePath}"));
builder.Services.AddHostedService<DatabaseInitializer>();

builder.Services.AddSingleton<ImagePreparer>();
builder.Services.AddSingleton<ExtractionQueue>();
builder.Services.AddHttpClient<ILlamaClient, LlamaClient>((sp, http) =>
{
    var llm = sp.GetRequiredService<IOptions<LlmOptions>>().Value;
    http.BaseAddress = new Uri(llm.BaseUrl.TrimEnd('/') + "/");
    http.Timeout = llm.Timeout;
    if (!string.IsNullOrEmpty(llm.ApiKey))
    {
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", llm.ApiKey);
    }
});
builder.Services.AddScoped<ReceiptExtractor>();
builder.Services.AddScoped<ReceiptService>();
// Registered after DatabaseInitializer so migrations are applied before unfinished receipts are re-queued.
builder.Services.AddHostedService<ExtractionWorker>();

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// The built React app (receiptsplit.client) is published into wwwroot.
app.UseDefaultFiles();
app.MapStaticAssets();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthorization();

app.MapControllers();

// Unknown API paths stay 404; every other unmatched path is a client-side route of the React app.
app.MapFallback("/api/{**path}", () => Results.NotFound());
app.MapFallbackToFile("/index.html");

app.Run();
