using SMSGateway.Core.Models;
using SMSGateway.Core.Interfaces;
using SmsGateway.Core;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.Configure<RateLimitConfig>(builder.Configuration.GetSection("RateLimit"));

builder.Services.AddSingleton<IRateLimitingService, SlidingWindowRateLimiter>();
builder.Services.AddHostedService<PurgeBackgroundService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment()) {
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();