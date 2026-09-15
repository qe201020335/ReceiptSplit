using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ReceiptSplit.Data;
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

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthorization();

app.MapControllers();

app.Run();
