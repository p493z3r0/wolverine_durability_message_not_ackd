using JasperFx;
using JasperFx.CodeGeneration;
using JasperFx.Core;
using Npgsql;
using wolverine_durable_messaging_issue;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.Postgresql;
using Wolverine.RabbitMQ;
using Wolverine.Shims.MassTransit;
using Wolverine.Transports.Sending;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
Console.WriteLine("Starting message sender");

builder.Services.AddWolverine(options =>
{

  options.Policies.LogMessageStarting(LogLevel.Trace);
        options.Policies.MessageExecutionLogLevel(LogLevel.Trace);
        options.Policies.MessageSuccessLogLevel(LogLevel.Trace);
        options.ServiceName = "sender";

        options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;

        options
            .Policies.OnException<TimeoutException>()
            .RetryWithCooldown(100.Milliseconds(), 1.Seconds(), 5.Seconds())
            .Then.MoveToErrorQueue();

        options
            .Policies.OnException<NpgsqlException>()
            .RetryWithCooldown(500.Milliseconds(), 5.Seconds(), 30.Seconds())
            .Then.MoveToErrorQueue();

       

        options.PersistMessagesWithPostgresql("Host=postgres-db;Port=5432;Database=postgres;Username=postgres;Password=1234;", "public");
        options.Policies.DisableConventionalLocalRouting();
        options.Policies.UseDurableInboxOnAllListeners();
        options.Policies.UseDurableLocalQueues();
        options.Policies.UseDurableOutboxOnAllSendingEndpoints();

        options.Policies.AutoApplyTransactions();

        options.MultipleHandlerBehavior = MultipleHandlerBehavior.Separated;
        options.Durability.MessageIdentity = MessageIdentity.IdAndDestination;

        options.Durability.InboxStaleTime = 30.Minutes();
        options.Durability.OutboxStaleTime = 30.Minutes();
        options.Durability.ScheduledJobPollingTime = 1.Seconds();
        options.Durability.NodeReassignmentPollingTime = 1.Seconds();

        options.Discovery.CustomizeHandlerDiscovery(x =>
        {
            x.Excludes.Implements(typeof(IConsumer<>));
        });
        options
            .UseRabbitMq(rabbitMqConfig =>
            {
                // Connection to "Main" RabbitMQ
                rabbitMqConfig.HostName = "rabbit-main"; // The service name in Docker
                rabbitMqConfig.Port = 5672;              // Internal container port
                rabbitMqConfig.UserName = "admin";
                rabbitMqConfig.Password = "password123";
                rabbitMqConfig.VirtualHost = "/";
                rabbitMqConfig.AutomaticRecoveryEnabled = true;
                rabbitMqConfig.RequestedHeartbeat = TimeSpan.FromSeconds(10);
            })
            .AddTenant(
                "shared",
                rabbitMqConfig =>
                {
                    // Connection to "Shared" RabbitMQ
                    rabbitMqConfig.HostName = "rabbit-shared"; // The service name in Docker
                    rabbitMqConfig.Port = 5672;                // Still 5672 inside the network
                    rabbitMqConfig.UserName = "guest_user";
                    rabbitMqConfig.Password = "guest_pass";
                    rabbitMqConfig.VirtualHost = "/";
                    rabbitMqConfig.AutomaticRecoveryEnabled = true;
                    rabbitMqConfig.RequestedHeartbeat = TimeSpan.FromSeconds(10);
                }
            )
            .UseConventionalRouting(conventions =>
            {
                
            })
            .AutoProvision()
            .TenantIdBehavior(TenantedIdBehavior.FallbackToDefault);

        options.CodeGeneration.TypeLoadMode = TypeLoadMode.Auto;
    
    
});
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
    {
        var forecast = Enumerable.Range(1, 5).Select(index =>
                new WeatherForecast
                (
                    DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                    Random.Shared.Next(-20, 55),
                    summaries[Random.Shared.Next(summaries.Length)]
                ))
            .ToArray();
        return forecast;
    })
    .WithName("GetWeatherForecast");

_ =  Task.Run(() =>
{
    Console.WriteLine("Starting");
    Task.Delay(TimeSpan.FromSeconds(5)).Wait();
    var scope = app.Services.CreateScope();
    var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
    bus.PublishAsync(new InitMessage()
    {
        Id = Guid.CreateVersion7()
    }).ConfigureAwait(false).GetAwaiter().GetResult();

});

app.RunJasperFxCommandsSynchronously(args);

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}