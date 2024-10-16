using Grpc.Core;
using Raft;
using Grpc.Net.Client;

namespace Services
{
    enum NodeState
    {
        Follower,
        Candidate,
        Leader
    }

    class Log<T>
    {
        private readonly List<T> _internalList = [];

        public T this[int index]
        {
            get
            {
                if (index < 1 || index > Count)
                    throw new ArgumentOutOfRangeException(nameof(index), "Index must be between 1 and Count.");
                return _internalList[index - 1]; // Adjust for 0-based indexing
            }
            set
            {
                if (index < 1 || index > Count)
                    throw new ArgumentOutOfRangeException(nameof(index), "Index must be between 1 and Count.");
                _internalList[index - 1] = value; // Adjust for 0-based indexing
            }
        }

        public List<T> BackingList => _internalList;

        public int Count => _internalList.Count;

        public void Add(T item)
        {
            _internalList.Add(item);
        }

        public void Clear()
        {
            _internalList.Clear();
        }

        public bool Contains(T item)
        {
            return _internalList.Contains(item);
        }

        public int IndexOf(T item)
        {
            int index = _internalList.IndexOf(item);
            return index >= 0 ? index + 1 : -1; // Convert to 1-based index
        }

        public void Insert(int index, T item)
        {
            if (index < 1 || index > Count + 1)
                throw new ArgumentOutOfRangeException(nameof(index), "Index must be between 1 and Count + 1.");
            _internalList.Insert(index - 1, item); // Adjust for 0-based indexing
        }

        public bool Remove(T item)
        {
            return _internalList.Remove(item);
        }

        public void RemoveAt(int index)
        {
            if (index < 1 || index > Count) throw new ArgumentOutOfRangeException(nameof(index), "Index must be between 1 and Count.");
            _internalList.RemoveAt(index - 1); // Adjust for 0-based indexing
        }

        public void RemoveRange(int index, int count)
        {
            _internalList.RemoveRange(index - 1, count);
        }
    }

    public class RaftService : RaftProtocol.RaftProtocolBase
    {
        private NodeState _state = NodeState.Follower;
        private int _currentTerm = 0;
        private string? _votedFor = null;
        private readonly Log<LogEntry> _log = new();
        private int _commitIndex = 0;
        private int _lastApplied = 0;
        private readonly string _id;

        private bool _electionInProgress = false;
        private readonly Random _random = new();
        private CancellationTokenSource _cancellationTokenSource;

        private const int MinElectionTimeout = 1500;
        private const int MaxElectionTimeout = 3000;
        private readonly List<string> _otherNodes;
        private const int HeartbeatInterval = 1000;

        private readonly Dictionary<string, int> _nextIndex = new();
        private readonly Dictionary<string, int> _matchIndex = new();

        public RaftService(List<string> otherNodes, string address)
        {
            _cancellationTokenSource = new CancellationTokenSource();
            StartElectionTimer(_cancellationTokenSource.Token);
            _otherNodes = otherNodes;
            _id = address;
        }

        private void StartElectionTimer(CancellationToken cancellationToken)
        {
            Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    int electionTimeout = _random.Next(MinElectionTimeout, MaxElectionTimeout);
                    Console.WriteLine($"{_id.Split(":")[2]}: Election timer set to {electionTimeout} ms");

                    try
                    {
                        await Task.Delay(electionTimeout, cancellationToken);

                        // If no heartbeat is received, start an election
                        if (_state != NodeState.Leader && !_electionInProgress)
                        {
                            await StartElection();
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        // Timer was canceled; this can happen on heartbeat reception
                    }
                }
            }, cancellationToken);
        }



        private async Task SendAppendEntriesAsync(string nodeAddress)
        {
            using var channel = GrpcChannel.ForAddress(nodeAddress);
            var client = new RaftProtocol.RaftProtocolClient(channel);

            // Check if the node has an entry in the nextIndex dictionary, initialize if not
            if (!_nextIndex.TryGetValue(nodeAddress, out int value))
            {
                value = _log.Count + 1;
                _nextIndex[nodeAddress] = value;  // Start with the last log index + 1 (no new entries yet)
            }
            // Create the AppendEntries request based on nextIndex
            var prevLogIndex = value - 1;  // The log index before the next entry we want to send
            var prevLogTerm = prevLogIndex >= 1 ? _log[prevLogIndex].Term : 0;

            var entries = new List<LogEntry>();
            for (int i = value; i <= _log.Count; i++)
            {
                entries.Add(_log[i]);
            }

            var request = new AppendEntriesRequest
            {
                Term = _currentTerm,
                LeaderId = _id,
                PrevLogIndex = prevLogIndex,
                PrevLogTerm = prevLogTerm,
                Entries = { entries },
                LeaderCommit = _commitIndex
            };

            try
            {
                var response = await client.AppendEntriesAsync(request);

                if (response.Success)
                {
                    Console.WriteLine($"{_id.Split(":")[2]}: AppendEntry to {nodeAddress} = Success!");
                    // If AppendEntries was successful, update nextIndex and matchIndex
                    _nextIndex[nodeAddress] = _log.Count + 1;          // Set nextIndex to the last log index + 1
                    _matchIndex[nodeAddress] = _log.Count;     // Set matchIndex to the last log entry
                }
                else
                {
                    Console.WriteLine($"{_id.Split(":")[2]}: AppendEntry to {nodeAddress} = Failure!");
                    // If AppendEntries failed (e.g., logs do not match), decrement nextIndex and try again later
                    _nextIndex[nodeAddress] = Math.Max(1, _nextIndex[nodeAddress] - 1);
                }
            }
            catch (RpcException ex)
            {
                Console.WriteLine($"Failed to send AppendEntries to {nodeAddress}: {ex.Status}");
            }
            finally
            {
                await channel.ShutdownAsync();
            }
        }


        private async Task SendHeartbeat(string nodeAddress)
        {
            using var channel = GrpcChannel.ForAddress(nodeAddress);
            var client = new RaftProtocol.RaftProtocolClient(channel);

            // Create an empty AppendEntries request for heartbeat
            var request = new AppendEntriesRequest
            {
                Term = _currentTerm,        // The leader's current term
                LeaderId = _id,             // The leader's unique ID (address)
                PrevLogIndex = _nextIndex[nodeAddress] - 1, // The index of the last log entry
                PrevLogTerm = _nextIndex[nodeAddress] - 1 >= 1 && _nextIndex[nodeAddress] - 1 <= _log.Count ? _log[_nextIndex[nodeAddress] - 1].Term : 0,   // The term of the last log entry
                LeaderCommit = _commitIndex, // The leader's commit index
                Entries = { }
            };

            try
            {
                // Send the AppendEntries RPC to the follower as a heartbeat
                var response = await client.AppendEntriesAsync(request);

                // Check the response and handle accordingly
                if (response.Success)
                {
                    Console.WriteLine($"Heartbeat to {nodeAddress} succeeded.");
                }
                else
                {
                    // If the follower's term is higher, we need to step down
                    if (response.Term > _currentTerm)
                    {
                        Console.WriteLine($"{_id.Split(":")[2]}: Follower {nodeAddress} has a higher term. Stepping down as leader.");
                        _currentTerm = response.Term;
                        BecomeFollower();
                    }
                    else
                    {
                        Console.WriteLine($"Heartbeat to {nodeAddress} failed.");
                        _nextIndex[nodeAddress] = Math.Max(1, _nextIndex[nodeAddress] - 1);
                    }
                }
            }
            catch (RpcException ex)
            {
                // Log the error in case the RPC fails (follower might be down or unreachable)
                Console.WriteLine($"Failed to send heartbeat to {nodeAddress}: {ex.Status}");
            }
            finally
            {
                await channel.ShutdownAsync();
            }
        }


        private void StartHeartbeatTimer()
        {
            Task.Run(async () =>
            {
                while (_state == NodeState.Leader)
                {
                    await Task.Delay(HeartbeatInterval); // Heartbeat interval

                    UpdateCommitIndex();

                    Console.WriteLine($"{_id.Split(":")[2]}: Leader Log: [{string.Join(",", _log.BackingList)}]");
                    foreach (var node in _otherNodes)
                    {
                        if (node != _id)
                        {
                            Console.WriteLine($"{_id.Split(":")[2]}: Sending heartbeat to {node}.");
                            // You may want to send an empty AppendEntries RPC for heartbeat
                            _ = SendAppendEntriesAsync(node);
                        }
                        else
                        {
                            _nextIndex[node] = _log.Count + 1;          // Set nextIndex to the last log index + 1
                            _matchIndex[node] = _log.Count;
                        }
                    }
                }
            });
        }

        private void UpdateCommitIndex()
        {
            int totalServers = _matchIndex.Count;

            // Iterate over possible log indices (starting from current commitIndex + 1)
            for (int N = _commitIndex + 1; N < _log.Count + 1; N++)
            {
                // Count how many servers have replicated the log entry at index N
                int matchCount = 1; // Start with 1 for the leader (itself)

                foreach (KeyValuePair<string, int> pair in _matchIndex)
                {
                    if (pair.Value >= N)
                    {
                        matchCount++;
                    }
                }

                // Check if a majority of servers have replicated this entry
                if (matchCount > totalServers / 2)
                {
                    // Check if log[N] is from the current term
                    if (_log[N].Term == _currentTerm)
                    {
                        // Update the commitIndex to N since it satisfies the conditions
                        _commitIndex = N;
                        ApplyLogEntries();

                    }
                }
            }

        }


        private void BecomeFollower()
        {
            _state = NodeState.Follower;
            _electionInProgress = false; // Election is not in progress since a leader is active
            _cancellationTokenSource.Cancel(); // Cancel the current timer
            _cancellationTokenSource = new CancellationTokenSource(); // Create a new token
            StartElectionTimer(_cancellationTokenSource.Token); // Restart the election timer
        }

        private async Task<RequestVoteResponse> SendRequestVoteAsync(string nodeAddress)
        {
            Console.WriteLine($"{_id.Split(":")[2]}: Sending RequestVote to " + nodeAddress);
            using var channel = GrpcChannel.ForAddress(nodeAddress);

            var client = new RaftProtocol.RaftProtocolClient(channel);
            var request = new RequestVoteRequest
            {
                Term = _currentTerm,
                CandidateId = _id, // Replace with actual ID
                LastLogIndex = _log.Count,
                LastLogTerm = _log.Count > 0 ? _log[_log.Count].Term : 0
            };

            try
            {
                var t = await client.RequestVoteAsync(request);
                if (t.VoteGranted)
                {
                    Console.WriteLine($"{_id.Split(":")[2]}: Vote received from " + nodeAddress);
                }
                return t;
            }
            catch (RpcException ex)
            {
                Console.WriteLine($"Failed to send RequestVote to {nodeAddress}: {ex.Status}");
                return new RequestVoteResponse { Term = _currentTerm, VoteGranted = false };
            }
            finally
            {
                await channel.ShutdownAsync();
            }
        }

        private async Task StartElection()
        {
            Console.WriteLine($"{_id.Split(":")[2]}: Election timeout expired. Starting election.");
            _state = NodeState.Candidate;
            _electionInProgress = true;
            _currentTerm++;
            _votedFor = _id; // Vote for ourselves

            int votesReceived = 1; // Count self-vote
            List<Task<RequestVoteResponse>> voteTasks = [];

            // Send RequestVote RPC to other nodes
            foreach (var node in _otherNodes)
            {
                if (node != _id)
                {
                    var task = SendRequestVoteAsync(node);
                    voteTasks.Add(task);
                }
            }

            // Wait for all votes to be collected
            var responses = await Task.WhenAll(voteTasks);

            // Count the votes
            foreach (var response in responses)
            {
                if (response.VoteGranted)
                {

                    votesReceived++;
                    Console.WriteLine($"{_id.Split(":")[2]}: Current Votes: {votesReceived}");
                }
            }

            // Determine if we won the election
            if (votesReceived > _otherNodes.Count / 2)
            {
                BecomeLeader();
            }
            else
            {
                Console.WriteLine($"{_id.Split(":")[2]}: Did not get required votes");
                _electionInProgress = false; // Election failed
            }
        }

        private void BecomeLeader()
        {
            Console.WriteLine($"{_id.Split(":")[2]}: Node has become the leader.");
            _log.Add(new LogEntry { Command = $"{_id.Split(":")[2]} is leader", Term = _currentTerm });
            // 1. Cancel the election timer
            _cancellationTokenSource.Cancel();

            // 2. Set the node state to Leader
            _state = NodeState.Leader;

            foreach (var node in _otherNodes)
            {
                // Initialize nextIndex to the index after the last log entry
                _nextIndex[node] = _log.Count + 1;

                // Initialize matchIndex to 0 (no log entries are replicated yet)
                _matchIndex[node] = 0;
            }

            // 4. Send initial heartbeats to all followers
            foreach (var node in _otherNodes)
            {
                if (node != _id)
                {
                    Console.WriteLine($"{_id.Split(":")[2]}: Sending initial heartbeat to {node}.");
                    // Send an empty AppendEntries message to establish leadership
                    Task t = SendHeartbeat(node);

                }
            }

            // 5. Start the heartbeat timer to continue sending heartbeats periodically
            StartHeartbeatTimer();
        }

        public override Task<AppendEntriesResponse> AppendEntries(AppendEntriesRequest request, ServerCallContext context)
        {
            Console.WriteLine($"{_id.Split(":")[2]}: Received AppendEntries from {request.LeaderId}\n{{ Term: {request.Term}, PrevLogIndex: {request.PrevLogIndex}, PrevLogTerm: {request.PrevLogTerm}, LeaderId: {request.LeaderId}, LeaderCommit: {request.LeaderCommit}, Entries: {request.Entries} }}");
            Console.WriteLine($"{_id.Split(":")[2]}: Log: [ {string.Join(",", _log.BackingList)} ]");
            BecomeFollower();

            // If the term of the leader is less than the current term, reject the request
            if (request.Term < _currentTerm)
            {
                Console.WriteLine($"{_id.Split(":")[2]}: Leader term is less than current term");
                return Task.FromResult(new AppendEntriesResponse { Term = _currentTerm, Success = false });
            }

            // Update current term if the leader has a newer term
            if (request.Term > _currentTerm)
            {
                _currentTerm = request.Term;
                _votedFor = null;
            }

            if (request.PrevLogIndex >= 1)
            {
                if (_log.Count < request.PrevLogIndex || _log[request.PrevLogIndex].Term != request.PrevLogTerm)
                {
                    Console.WriteLine($"{_id.Split(":")[2]}: AppendEntries failed. Log doesn't contain an entry at prevLogIndex with prevLogTerm");
                    return Task.FromResult(new AppendEntriesResponse { Term = _currentTerm, Success = false });
                }
            }

            // Append any new entries from the leader that aren't already in the log
            int index = request.PrevLogIndex + 1;
            for (int i = 0; i < request.Entries.Count; i++, index++)
            {
                if (_log.Count >= index)
                {
                    // If an existing entry conflicts with a new one, delete the existing entry and all that follow it
                    if (_log[index + 1].Term != request.Entries[i].Term)
                    {
                        _log.RemoveRange(index + 1, _log.Count - index);
                    }
                }

                // Append any new entries not in the log
                if (_log.Count < index)
                {
                    _log.Add(request.Entries[i]);
                }
            }

            // If leaderCommit > commitIndex, set commitIndex to min(leaderCommit, index of last new entry)
            if (request.LeaderCommit > _commitIndex)
            {
                _commitIndex = Math.Min(request.LeaderCommit, _log.Count);
            }

            // Apply committed log entries
            ApplyLogEntries();

            Console.WriteLine($"{_id.Split(":")[2]}: Current Log [{string.Join(";", _log.BackingList)}]");

            return Task.FromResult(new AppendEntriesResponse { Term = _currentTerm, Success = true });
        }

        private void ApplyLogEntries()
        {
            // Apply all entries up to the commit index
            while (_lastApplied < _commitIndex)
            {
                _lastApplied++;
                // Apply _log[_lastApplied] to the state machine
                // (In a real Raft system, this would involve applying the command to the actual state machine)
                Console.WriteLine($"Applying log entry at index {_lastApplied}, term {_log[_lastApplied].Term}");
            }
        }

        public override Task<RequestVoteResponse> RequestVote(RequestVoteRequest request, ServerCallContext context)
        {
            Console.WriteLine($"{_id.Split(":")[2]}: Received RequestVote from {request.CandidateId.Split(":")[2]}");
            // Initialize the response
            var response = new RequestVoteResponse
            {
                Term = _currentTerm,
                VoteGranted = false
            };

            // 1. Reply false if candidate’s term is less than the follower's current term
            if (request.Term < _currentTerm)
            {
                return Task.FromResult(response);
            }

            // 2. If the term is higher, update the current term and reset who we've voted for
            if (request.Term > _currentTerm)
            {
                _currentTerm = request.Term;
                _votedFor = null; // Reset the vote for this new term
            }

            // 3. Check if we have already voted for another candidate in this term
            if (_votedFor == null || _votedFor == request.CandidateId)
            {
                // 4. Check if the candidate’s log is at least as up-to-date as our log
                bool logIsUpToDate = IsLogUpToDate(request.LastLogTerm, request.LastLogIndex);

                if (logIsUpToDate)
                {
                    // Grant vote and record that we've voted for this candidate
                    _votedFor = request.CandidateId;
                    response.VoteGranted = true;

                    BecomeFollower();
                }
            }

            // Return the response with updated term and vote decision
            response.Term = _currentTerm;
            return Task.FromResult(response);
        }

        private bool IsLogUpToDate(int candidateLastLogTerm, int candidateLastLogIndex)
        {
            // If our log is empty, the candidate's log is automatically considered up-to-date
            if (_log.Count == 0) return true;

            // Get the last log entry term and index from the follower's log
            var lastLogEntry = _log[_log.Count];
            int lastTerm = lastLogEntry.Term;
            int lastIndex = _log.Count;

            // Compare the candidate's last log term and index with ours
            // The candidate's log is considered more up-to-date if:
            // 1. Its last term is greater than our last term
            // 2. If the last terms are equal, the candidate's log is at least as long as ours
            if (candidateLastLogTerm > lastTerm)
            {
                return true;
            }
            else if (candidateLastLogTerm == lastTerm && candidateLastLogIndex >= lastIndex)
            {
                return true;
            }

            return false;
        }
    }
}
