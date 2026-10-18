using System.Text.Json.Serialization;
using Microsoft.AspNetCore.HttpLogging;
using MarketsEngine.Infrastructure;
using MarketsEngine.Api.Seeding;
using MarketsEngine.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddProblemDetails();
builder.Services.AddHttpLogging(options =>
    options.LoggingFields = HttpLoggingFields.RequestMethod | HttpLoggingFields.RequestPath |
                             HttpLoggingFields.ResponseStatusCode | HttpLoggingFields.Duration);

builder.Services.AddInfrastructure(builder.Configuration);

var postgresConnectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException(
        "Missing 'ConnectionStrings:Postgres'. Set it with: dotnet user-secrets set \"ConnectionStrings:Postgres\" \"...\"");

builder.Services.AddHealthChecks()
    .AddNpgSql(postgresConnectionString, name: "postgres");

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddHostedService<MockQuoteRefresherService>();
}

var app = builder.Build();

app.UseExceptionHandler();
app.UseHttpLogging();

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

if (app.Environment.IsDevelopment())
{
    await MockDataSeeder.SeedAsync(app.Services);
}

app.Run();

public partial class Program;
