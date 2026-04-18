using Godot;
using System.Collections.Generic;

/// <summary>
/// Attached to BattleScene.tscn. Runs only on the server/host.
///
/// On scene load the server dynamically instances the stage selected in
/// NetworkManager.SelectedStage, then spawns one character per connected peer.
/// The MultiplayerSpawner sibling replicates spawned characters to all clients.
///
/// Spawn positions are read from a "SpawnPoints" node inside each stage scene.
/// Stage makers: add a Node2D named "SpawnPoints" with Marker2D children named
/// "Spawn1", "Spawn2", "Spawn3", "Spawn4" at the desired player start positions.
///
/// Scene setup required in the editor:
///   BattleScene (Node2D)  — this script
///     MultiplayerSpawner  — spawnable_scenes: all 4 character .tscn paths
///                           + SteampunkProjectile.tscn + Halberd.tscn
///                           spawn_path = NodePath(".")
///     (stage is added at runtime by _Ready)
/// </summary>
public partial class BattleManager : Node2D
{
    // -------------------------------------------------------------------------
    // Character scene paths
    // -------------------------------------------------------------------------
    private static readonly Dictionary<string, string> CharacterScenePaths = new()
    {
        { "KernelCowboy", "res://Scenes/KernelCowboy/KernelCowboy.tscn" },
        { "SirEdward",    "res://Scenes/Edward/Edward.tscn"              },
        { "Steampunk",    "res://Scenes/Steampunk/Steampunk.tscn"        },
        { "Vampire",      "res://Scenes/Vampire/Vampire.tscn"            },
    };

    // Cached after the stage is loaded.
    private Vector2[] _spawnPoints = [];

    // Ready handshake — host waits for all peers before spawning.
    private int _readyCount;
    private int _expectedPlayers;

    private MultiplayerSpawner _spawner;

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------
    public override void _Ready()
    {
        _spawner = GetNode<MultiplayerSpawner>("MultiplayerSpawner");
        // Fires on both host (when it adds a node) and client (when spawn is received).
        // Used to re-attach MultiplayerSynchronizer AFTER the spawn packet is sent,
        // preventing the race where path registration arrives before the spawn.
        _spawner.Spawned += OnCharacterSpawned;

        LoadStage();

        if (Multiplayer.IsServer())
        {
            _expectedPlayers = NetworkManager.Instance.ConnectedPeers.Count;
            NetworkManager.Instance.PeerLeft += OnPeerLeft;
            RegisterReady();
        }
        else
        {
            RpcId(1, nameof(NotifyReady));
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
         TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void NotifyReady() => RegisterReady();

    private void RegisterReady()
    {
        _readyCount++;
        GD.Print($"[BattleManager] Ready {_readyCount}/{_expectedPlayers}");

        if (_readyCount < _expectedPlayers) return;

        // All peers have loaded — safe to spawn; MultiplayerSpawner will replicate.
        int index = 0;
        foreach (long peerId in NetworkManager.Instance.ConnectedPeers)
        {
            SpawnCharacter(peerId, index % _spawnPoints.Length);
            index++;
        }
    }

    // -------------------------------------------------------------------------
    // Stage loading
    // -------------------------------------------------------------------------

    /// <summary>
    /// Instances the stage scene chosen in NetworkManager.SelectedStage and reads
    /// its "SpawnPoints" child node to populate _spawnPoints.
    ///
    /// Stage convention: each stage scene must have a Node2D named "SpawnPoints"
    /// with Marker2D children named "Spawn1" through "Spawn4".
    /// </summary>
    private void LoadStage()
    {
        string stagePath = NetworkManager.Instance.SelectedStage;
        var stageScene = GD.Load<PackedScene>(stagePath);
        if (stageScene == null)
        {
            GD.PushError($"[BattleManager] Could not load stage: {stagePath}");
            return;
        }

        Node stage = stageScene.Instantiate();
        AddChild(stage);

        // Read spawn positions from the stage's SpawnPoints node.
        Node spawnRoot = stage.GetNodeOrNull("SpawnPoints");
        if (spawnRoot == null)
        {
            GD.PushWarning($"[BattleManager] Stage '{stagePath}' has no 'SpawnPoints' node. " +
                           "Add a Node2D named 'SpawnPoints' with Marker2D children Spawn1-Spawn4.");
            return;
        }

        var points = new List<Vector2>();
        for (int i = 1; i <= 4; i++)
        {
            var marker = spawnRoot.GetNodeOrNull<Marker2D>($"Spawn{i}");
            if (marker != null)
                points.Add(marker.GlobalPosition);
        }

        if (points.Count == 0)
            GD.PushWarning($"[BattleManager] SpawnPoints node in '{stagePath}' has no Marker2D children.");

        _spawnPoints = [..points];
    }

    // -------------------------------------------------------------------------
    // Spawning
    // -------------------------------------------------------------------------
    private void SpawnCharacter(long peerId, int spawnIndex)
    {
        string charName = NetworkManager.Instance.PeerCharacters.TryGetValue(peerId, out string c)
            ? c : "KernelCowboy";

        if (!CharacterScenePaths.TryGetValue(charName, out string path))
        {
            GD.PushError($"[BattleManager] Unknown character name: '{charName}' for peer {peerId}");
            return;
        }

        var scene = GD.Load<PackedScene>(path);
        if (scene == null)
        {
            GD.PushError($"[BattleManager] Could not load scene at: {path}");
            return;
        }

        var node = scene.Instantiate<CharacterBase>();
        node.Name = peerId.ToString();

        if (_spawnPoints.Length > 0)
            node.GlobalPosition = _spawnPoints[spawnIndex];

        node.AddToGroup("characters");

        // Strip the embedded MultiplayerSynchronizer before the node enters the tree.
        // In Godot 4, a child's NOTIFICATION_ENTER_TREE fires before child_entered_tree
        // on the parent, so the sync would send its path registration to clients BEFORE
        // MultiplayerSpawner sends the spawn packet — clients would fail to find the node.
        // We re-attach it inside OnCharacterSpawned, which fires after the spawn packet.
        var sync = node.GetNodeOrNull<MultiplayerSynchronizer>("MultiplayerSynchronizer");
        if (sync != null)
        {
            node.RemoveChild(sync);
            sync.Free();
        }

        CallDeferred(Node.MethodName.AddChild, node);
        GD.Print($"[BattleManager] Spawning {charName} for peer {peerId} at {node.GlobalPosition}");
    }

    // -------------------------------------------------------------------------
    // Sync re-attachment
    // -------------------------------------------------------------------------

    // Called on BOTH host and client after a character enters the scene tree and
    // MultiplayerSpawner has already sent (or received) the spawn packet.
    // We rebuild the MultiplayerSynchronizer here so its path registration is
    // delivered to peers that already have the character node.
    private void OnCharacterSpawned(Node node)
    {
        if (node is not CharacterBase character) return;
        if (character.GetNodeOrNull<MultiplayerSynchronizer>("MultiplayerSynchronizer") != null) return;

        var config = new SceneReplicationConfig();

        config.AddProperty(new NodePath(".:position"));
        config.PropertySetSpawn(new NodePath(".:position"), true);
        config.PropertySetReplicationMode(new NodePath(".:position"), SceneReplicationConfig.ReplicationMode.Always);

        config.AddProperty(new NodePath(".:velocity"));
        config.PropertySetSpawn(new NodePath(".:velocity"), false);
        config.PropertySetReplicationMode(new NodePath(".:velocity"), SceneReplicationConfig.ReplicationMode.Always);

        config.AddProperty(new NodePath(".:CurrentState"));
        config.PropertySetSpawn(new NodePath(".:CurrentState"), true);
        config.PropertySetReplicationMode(new NodePath(".:CurrentState"), SceneReplicationConfig.ReplicationMode.OnChange);

        config.AddProperty(new NodePath(".:CurrentHP"));
        config.PropertySetSpawn(new NodePath(".:CurrentHP"), true);
        config.PropertySetReplicationMode(new NodePath(".:CurrentHP"), SceneReplicationConfig.ReplicationMode.OnChange);

        var sync = new MultiplayerSynchronizer();
        sync.Name = "MultiplayerSynchronizer";
        sync.ReplicationConfig = config;
        character.AddChild(sync);
        GD.Print($"[BattleManager] Attached MultiplayerSynchronizer to {character.Name}");
    }

    // -------------------------------------------------------------------------
    // Peer disconnect during match
    // -------------------------------------------------------------------------
    private void OnPeerLeft(long id)
    {
        if (!Multiplayer.IsServer()) return;

        Node character = GetNodeOrNull(id.ToString());
        if (character != null)
        {
            character.QueueFree();
            GD.Print($"[BattleManager] Removed character for disconnected peer {id}");
        }
    }
}
