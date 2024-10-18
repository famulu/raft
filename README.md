# Raft Consensus Algorithm Implementation

This project is a C# implementation of the Raft consensus algorithm using gRPC. It is designed to support distributed systems where multiple nodes work together to ensure consistency in a fault-tolerant manner. Raft is used to manage replicated logs and reach consensus on distributed operations.

## Project Structure

- `Services/RaftService.cs`: The core service that handles the Raft protocol. It includes node states, log management, and communication between nodes.
- `Proto/raft.proto`: Schema for gRPC whic is used to send and receive `AppendEntries` and `RequestVote` messages between nodes.

## Running the Application

The application expects a command-line argument that specifies the index of the node to run. This index determines which port (URL) the node will use.

Example to start a node on https://localhost:5000:

```sh
dotnet run -- 0
```

You can run multiple nodes by providing different indexes (0-4):

```sh
dotnet run -- 0  # Runs node on https://localhost:5000
```

```sh
dotnet run -- 1  # Runs node on https://localhost:5001
```

## Features

### Node States

Each node can be in one of the following states:

- **Follower**: Listens to heartbeats and log replication requests from the leader.
- **Candidate**: A node transitions to a candidate when it times out waiting for a heartbeat, initiating a new election.
- **Leader**: Responsible for sending heartbeats and log replication requests to followers, ensuring that the log entries are consistent across the cluster.

### Election Process

When a follower times out without receiving a heartbeat from the leader, it becomes a candidate and starts an election. The candidate requests votes from other nodes by sending `RequestVote` messages. If the candidate receives votes from a majority of the nodes, it becomes the leader. If another node has a higher term, the candidate steps down and becomes a follower again.

### Log Replication

- The leader replicates log entries to followers through `AppendEntries` RPC calls.
- Followers append log entries and update their commit index based on the leader's commit index.
- If a follower's log is inconsistent with the leader's, it rejects the `AppendEntries` request, and the leader decrements its `nextIndex` for that follower to retry with the previous entries.

### Heartbeats

The leader sends periodic heartbeats (empty `AppendEntries` RPC calls) to its followers to maintain its leadership. Followers reset their election timers upon receiving these heartbeats.

### Fault Tolerance

- Raft can tolerate up to `(N-1)/2` failures in a cluster of `N` nodes.
- If a leader fails, a new leader is elected from the remaining nodes in the cluster.
  
## Classes and Components

### `RaftService`

This class implements the core functionality of the Raft protocol, including:

- **Election Timer**: Starts a timer to trigger an election if no heartbeats are received.
- **AppendEntries RPC**: Handles log replication and heartbeats from the leader.
- **RequestVote RPC**: Manages election votes for the candidate requesting leadership.
- **Log Management**: Keeps track of the node's log of commands (i.e., state machine entries).
- **Leadership Election**: Manages the process of starting elections and winning leadership through majority votes.

### `Log<T>`

A custom log class used for managing entries in the Raft log. Provides features such as:

- One-based indexing.
- Adding, removing, and retrieving log entries.
- Handling log conflicts by removing conflicting entries.

### gRPC Protocol

The project uses gRPC to facilitate communication between nodes. There are two main RPCs defined in the Raft protocol:

- **AppendEntries**: Used by the leader to replicate log entries and send heartbeats to followers.
- **RequestVote**: Used by candidates to gather votes during an election.

## Raft Protocol Overview

The Raft consensus algorithm is divided into the following phases:

1. **Leader Election**: One node is elected as the leader, responsible for managing log replication.
2. **Log Replication**: The leader replicates its log entries to other nodes, ensuring consistency.
3. **Fault Tolerance**: If the leader fails, a new election is held, and the cluster recovers without data loss.

## References

- [In Search of an Understandable Consensus Algorithm (Raft)](https://raft.github.io/raft.pdf): The original Raft paper explaining the algorithm.
- [gRPC Documentation](https://grpc.io/docs/): For more details on how gRPC works.

## License

This project is licensed under the MIT License.
