using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.OpenApi;
using MarketsEngine.Api.Auth;
using MarketsEngine.Infrastructure;
using MarketsEngine.Api.Seeding;
using MarketsEngine.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition(ApiKeyAuthenticationHandler.SchemeName, new OpenApiSecurityScheme
    {
        Name = ApiKeyAuthenticationHandler.HeaderName,
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Description = "API key required for every endpoint except /health. Set via dotnet user-secrets \"Auth:ApiKey\"."
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference(ApiKeyAuthenticationHandler.SchemeName, document)] = []
    });
});

builder.Services.AddProblemDetails();
builder.Services.AddHttpLogging(options =>
    options.LoggingFields = HttpLoggingFields.RequestMethod | HttpLoggingFields.RequestPath |
                             HttpLoggingFields.ResponseStatusCode | HttpLoggingFields.Duration);

builder.Services.AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
    .AddScheme<ApiKeyAuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, _ => { });

builder.Services.AddAuthorization(options =>
    options.FallbackPolicy = new AuthorizationPolicyBuilder(ApiKeyAuthenticationHandler.SchemeName)
        .RequireAuthenticatedUser()
        .Build());

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
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();

if (app.Environment.IsDevelopment())
{
    await MockDataSeeder.SeedAsync(app.Services);
}

app.Run();

public partial class Program;
