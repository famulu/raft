using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using RaftProtocolServer;
using Microsoft.Extensions.Hosting;
using Serilog;
using Microsoft.Extensions.Logging;

Log.Logger = new LoggerConfiguration().WriteTo.File("logs/raft.log", rollingInterval: RollingInterval.Day).CreateLogger();

string[] urls = ["https://localhost:5000", "https://localhost:5001", "https://localhost:5002", "https://localhost:5003", "https://localhost:5004"];
List<Task> tasks = [];
for (int i = 0; i < urls.Length; i++)
{
    string url = urls[i];
    var builder = WebApplication.CreateBuilder(args);

    builder.Logging.ClearProviders();
    // builder.Logging.AddSerilog();

    builder.Services.AddGrpc();
    builder.Services.AddSingleton<RaftService>();

    builder.Services.AddSingleton(url);
    builder.Services.AddSingleton<List<string>>([.. urls]);

    var app = builder.Build();
    app.Urls.Add(url);
    app.MapGrpcService<RaftService>();

    // Eagerly resolve RaftService to force construction
    using (var scope = app.Services.CreateScope())
    {
        var raftService = scope.ServiceProvider.GetRequiredService<RaftService>();
        // Optionally log or perform some initialization here
    }

    app.Start();
    tasks.Add(app.WaitForShutdownAsync());

}

await Task.WhenAll(tasks);
