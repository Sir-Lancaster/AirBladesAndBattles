using Godot;
using System.Collections.Generic;

/// <summary>
/// Autoload singleton. Manages the ENet UDP peer lifetime — hosting, joining, and
/// disconnecting. Does not contain any game-logic; higher-level scripts (BattleManager,
/// the UI scene) call into this to start or stop a session.
///
/// Register in project.godot:
///   [autoload]
///   NetworkManager="*res://Scripts/Multiplayer/NetworkManager.cs"
/// </summary>
public partial class NetworkManager : Node
{
    // -------------------------------------------------------------------------
    // Singleton
    // -------------------------------------------------------------------------
    public static NetworkManager Instance { get; private set; }

    // -------------------------------------------------------------------------
    // Configuration
    // -------------------------------------------------------------------------
    [Export] public int Port     = 8000;
    [Export] public int MaxPeers = 4;

    // -------------------------------------------------------------------------
    // Read-only state
    // -------------------------------------------------------------------------
    public bool IsHost  { get; private set; }
    public string HostIp { get; private set; } = "";
    public bool IsConnected =>
        Multiplayer.MultiplayerPeer != null &&
        Multiplayer.MultiplayerPeer.GetConnectionStatus() ==
            MultiplayerPeer.ConnectionStatus.Connected;

    /// <summary>
    /// The stage scene path to load when the battle starts.
    /// Set this from the UI before calling StartBattle().
    /// Defaults to Clocktower.
    /// </summary>
    public string SelectedStage { get; set; } = "res://Scenes/Stages/Clocktower.tscn";

    /// <summary>
    /// Maps peer ID → character name chosen in the lobby UI (e.g. "KernelCowboy").
    /// The UI scene is responsible for populating this before StartBattle() is called.
    /// Clients send their choice to the host; the host stores all choices here.
    /// </summary>
    public Dictionary<long, string> PeerCharacters { get; } = new();

    private readonly HashSet<long> _connectedPeers = new();

    /// <summary>All currently connected peer IDs, including the local peer.</summary>
    public IReadOnlyCollection<long> ConnectedPeers => _connectedPeers;

    // -------------------------------------------------------------------------
    // Signals
    // -------------------------------------------------------------------------
    [Signal] public delegate void ConnectedToServerEventHandler();
    [Signal] public delegate void ConnectionFailedEventHandler();
    [Signal] public delegate void PeerJoinedEventHandler(long id);
    [Signal] public delegate void PeerLeftEventHandler(long id);
    [Signal] public delegate void ServerClosedEventHandler();

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------
    public override void _Ready()
    {
        Instance = this;

        Multiplayer.PeerConnected      += OnPeerConnected;
        Multiplayer.PeerDisconnected   += OnPeerDisconnected;
        Multiplayer.ConnectedToServer  += OnConnectedToServer;
        Multiplayer.ConnectionFailed   += OnConnectionFailed;
        Multiplayer.ServerDisconnected += OnServerDisconnected;
    }

    // -------------------------------------------------------------------------
    // Public API — called by the UI scene
    // -------------------------------------------------------------------------

    /// <summary>
    /// Start hosting. Creates an ENet server on <see cref="Port"/>.
    /// Returns Error.Ok on success; the caller should show an error message otherwise.
    /// </summary>
    public Error StartHost()
    {
        var peer = new ENetMultiplayerPeer();
        Error err = peer.CreateServer(Port, MaxPeers);
        if (err != Error.Ok)
        {
            GD.PushError($"[NetworkManager] CreateServer failed: {err}");
            return err;
        }

        Multiplayer.MultiplayerPeer = peer;
        IsHost = true;
        HostIp = "";

        // Godot's PeerConnected signal does NOT fire for the host itself — register manually.
        _connectedPeers.Add(1L);
        EmitSignal(SignalName.PeerJoined, 1L);

        GD.Print($"[NetworkManager] Hosting on port {Port}");
        return Error.Ok;
    }

    /// <summary>
    /// Join a hosted game by IP address. Port must match the host's Port value.
    /// The ConnectedToServer / ConnectionFailed signal will fire asynchronously.
    /// </summary>
    public Error JoinGame(string ip)
    {
        var peer = new ENetMultiplayerPeer();
        Error err = peer.CreateClient(ip, Port);
        if (err != Error.Ok)
        {
            GD.PushError($"[NetworkManager] CreateClient failed: {err}");
            return err;
        }

        Multiplayer.MultiplayerPeer = peer;
        IsHost  = false;
        HostIp  = ip;

        GD.Print($"[NetworkManager] Connecting to {ip}:{Port} ...");
        return Error.Ok;
    }

    /// <summary>
    /// Close the current peer and reset all state.
    /// Safe to call when not connected.
    /// </summary>
    public void Disconnect()
    {
        if (Multiplayer.MultiplayerPeer == null) return;

        Multiplayer.MultiplayerPeer.Close();
        Multiplayer.MultiplayerPeer = null;

        _connectedPeers.Clear();
        PeerCharacters.Clear();
        IsHost  = false;
        HostIp  = "";

        GD.Print("[NetworkManager] Disconnected.");
    }

    /// <summary>
    /// Host-only. Sends all connected peers (including the host) to BattleScene.
    /// Call this from the UI after everyone has connected and chosen a character.
    /// </summary>
    public void StartBattle()
    {
        if (!IsHost) return;
        Rpc(MethodName.LoadBattleScene);
    }

    // -------------------------------------------------------------------------
    // Internal — Multiplayer event handlers
    // -------------------------------------------------------------------------

    private void OnPeerConnected(long id)
    {
        _connectedPeers.Add(id);
        GD.Print($"[NetworkManager] Peer connected: {id}");
        EmitSignal(SignalName.PeerJoined, id);
    }

    private void OnPeerDisconnected(long id)
    {
        _connectedPeers.Remove(id);
        PeerCharacters.Remove(id);
        GD.Print($"[NetworkManager] Peer disconnected: {id}");
        EmitSignal(SignalName.PeerLeft, id);
    }

    private void OnConnectedToServer()
    {
        long myId = Multiplayer.GetUniqueId();
        _connectedPeers.Add(myId);
        GD.Print($"[NetworkManager] Connected to server. My peer ID: {myId}");
        EmitSignal(SignalName.ConnectedToServer);
    }

    private void OnConnectionFailed()
    {
        GD.PushError("[NetworkManager] Connection failed.");
        EmitSignal(SignalName.ConnectionFailed);
    }

    private void OnServerDisconnected()
    {
        _connectedPeers.Clear();
        PeerCharacters.Clear();
        GD.Print("[NetworkManager] Server closed.");
        EmitSignal(SignalName.ServerClosed);
    }

    // -------------------------------------------------------------------------
    // Scene transitions — each public method is host-only and triggers the
    // matching RPC which runs on every peer (including the host via CallLocal).
    // -------------------------------------------------------------------------

    /// <summary>Host-only. Sends all peers to StageSelect when the host is ready to begin.</summary>
    public void StartStageSelect()
    {
        if (!IsHost) return;
        Rpc(MethodName.LoadStageSelectScene);
    }

    /// <summary>Host-only. Sends all peers to MultiplayerCharacterSelect after stage is chosen.</summary>
    public void StartCharacterSelect()
    {
        if (!IsHost) return;
        Rpc(MethodName.LoadCharacterSelectScene);
    }

    /// <summary>Host-only. Sends all peers to BattleScene after characters are chosen.</summary>
    public void StartBattle()
    {
        if (!IsHost) return;
        Rpc(MethodName.LoadBattleScene);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
    private void LoadStageSelectScene()
    {
        GetTree().ChangeSceneToFile("res://Scenes/Pages/StageSelect/StageSelect.tscn");
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
    private void LoadCharacterSelectScene()
    {
        GetTree().ChangeSceneToFile("res://Scenes/Pages/Menu/MultiplayerCharacterSelect.tscn");
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
    private void LoadBattleScene()
    {
        GetTree().ChangeSceneToFile("res://Scenes/Multiplayer/BattleScene.tscn");
    }
}
