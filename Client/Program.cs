using Grpc.Net.Client;
using Raft;

Console.WriteLine("Press any key to continue...");
Console.ReadKey();
using var channel = GrpcChannel.ForAddress("https://localhost:5000");
var client = new RaftProtocol.RaftProtocolClient(channel);

var reply = await client.RequestVoteAsync(new RequestVoteRequest {CandidateId = "meow", LastLogIndex = 10, LastLogTerm = 2, Term = 3});
Console.WriteLine("Greeting: " + reply.VoteGranted);

Console.WriteLine("Shutting down");
Console.WriteLine("Press any key to exit...");
Console.ReadKey();
