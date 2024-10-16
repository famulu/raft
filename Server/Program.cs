using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Server;
using Greet;
using Grpc.Net.Client;
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
    builder.Logging.AddSerilog();

    builder.Services.AddGrpc();
    builder.Services.AddSingleton<RaftService>();

    builder.Services.AddSingleton<string>(url);
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


// Greeter Code!
// var builder = WebApplication.CreateBuilder(args);
// builder.Services.AddGrpc();


// var app = builder.Build();
// app.MapGrpcService<GreeterService>();

// Task t = Task.Run(() => { app.Run(); });


// Console.WriteLine("Press any key to continue...");
// Console.ReadKey();
// using var channel = GrpcChannel.ForAddress("https://localhost:5001");
// var client = new Greeter.GreeterClient(channel);

// var reply = await client.SayHelloAsync(new HelloRequest { Name = "GreeterClient" });
// Console.WriteLine("Greeting: " + reply.Message);

// Console.WriteLine("Shutting down");
// Console.WriteLine("Press any key to exit...");
// Console.ReadKey();


