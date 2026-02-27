using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SnomedSearch.Core.Interfaces;
using SnomedSearch.Infrastructure.Data;
using SnomedSearch.Infrastructure.Services;
using dotenv.net;
using System;

DotEnv.Load();

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure SNOMED Repository
string host = Environment.GetEnvironmentVariable("SNOMED_DB_HOST") ?? "localhost";
string port = Environment.GetEnvironmentVariable("SNOMED_DB_PORT") ?? "5433";
string db = Environment.GetEnvironmentVariable("SNOMED_DB_NAME") ?? "niramoy";
string user = Environment.GetEnvironmentVariable("SNOMED_DB_USER") ?? "niramoy";
string pass = Environment.GetEnvironmentVariable("SNOMED_DB_PASSWORD") ?? "niramoy";

string connString = $"Host={host};Port={port};Database={db};Username={user};Password={pass};";

builder.Services.AddScoped<IAIService, MockAnthropicAIService>();
builder.Services.AddScoped<ISnomedRepository>(sp => new SnomedRepository(connString, sp.GetRequiredService<IAIService>()));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();
app.MapControllers();

app.Run();
