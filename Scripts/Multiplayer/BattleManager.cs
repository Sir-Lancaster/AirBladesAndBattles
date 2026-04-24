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
///     MultiplayerSpawner  — SpawnFunction is set in code (no spawnable_scenes needed)
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
        _spawner.Spawned += OnCharacterSpawned;

        // Custom spawn function: the host calls _spawner.Spawn(data), which sends
        // the data string to every peer and runs this function on each of them.
        // This means no scenes need to be listed in spawnable_scenes in the editor.
        _spawner.SpawnFunction = new Callable(this, nameof(CustomSpawn));

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
    private void LoadStage()
    {
        string stagePath = NetworkManager.Instance.SelectedStage;
        if (string.IsNullOrEmpty(stagePath))
        {
            GD.PushError("[BattleManager] SelectedStage is not set — host must call SetStage() before StartBattle().");
            return;
        }

        var stageScene = GD.Load<PackedScene>(stagePath);
        if (stageScene == null)
        {
            GD.PushError($"[BattleManager] Could not load stage: {stagePath}");
            return;
        }

        Node stage = stageScene.Instantiate();
        AddChild(stage);

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

    // Called only on the host. Sends spawn data to all peers via the spawner.
    private void SpawnCharacter(long peerId, int spawnIndex)
    {
        string charName = NetworkManager.Instance.PeerCharacters.TryGetValue(peerId, out string c)
            ? c : "KernelCowboy";

        if (!CharacterScenePaths.ContainsKey(charName))
        {
            GD.PushError($"[BattleManager] Unknown character name: '{charName}' for peer {peerId}");
            return;
        }

        // Pack all data needed to reconstruct the character on any peer.
        string spawnData = $"{peerId}:{charName}:{spawnIndex}";
        GD.Print($"[BattleManager] Spawning {charName} for peer {peerId}");
        _spawner.Spawn(spawnData);
    }

    // Runs on every peer (host and all clients) when Spawn() is called.
    // Must return a Node; the spawner adds it as a child of spawn_path.
    private Node CustomSpawn(Variant data)
    {
        string[] parts = data.AsString().Split(':');
        if (parts.Length < 3)
        {
            GD.PushError($"[BattleManager] Malformed spawn data: {data}");
            return null;
        }

        long peerId    = long.Parse(parts[0]);
        string charName   = parts[1];
        int spawnIndex = int.Parse(parts[2]);

        if (!CharacterScenePaths.TryGetValue(charName, out string path))
        {
            GD.PushError($"[BattleManager] Unknown character '{charName}' in CustomSpawn");
            return null;
        }

        var scene = GD.Load<PackedScene>(path);
        if (scene == null)
        {
            GD.PushError($"[BattleManager] Could not load scene: {path}");
            return null;
        }

        var node = scene.Instantiate<CharacterBase>();
        node.Name = peerId.ToString();

        if (_spawnPoints.Length > 0)
            node.Position = _spawnPoints[spawnIndex];

        node.AddToGroup("characters");

        // Remove any embedded MultiplayerSynchronizer — state is synced via SyncState RPC.
        var sync = node.GetNodeOrNull<MultiplayerSynchronizer>("MultiplayerSynchronizer");
        if (sync != null)
        {
            node.RemoveChild(sync);
            sync.Free();
        }

        GD.Print($"[BattleManager] CustomSpawn: {charName} (peer {peerId}) at {node.Position}");
        return node;
    }

    // -------------------------------------------------------------------------
    // Sync re-attachment
    // -------------------------------------------------------------------------
    private void OnCharacterSpawned(Node node)
    {
        if (node is not CharacterBase character) return;
        var sync = character.GetNodeOrNull<MultiplayerSynchronizer>("MultiplayerSynchronizer");
        if (sync == null) return;
        character.RemoveChild(sync);
        sync.Free();
        GD.Print($"[BattleManager] Removed embedded sync from {character.Name} — using RPC sync.");
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
