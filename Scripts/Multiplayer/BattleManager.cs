using Godot;
using System.Collections.Generic;
using System.Linq;

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
///     GameHUD (CanvasLayer) — drag into _gameHUD export
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

    // -------------------------------------------------------------------------
    // Spawner / stage state
    // -------------------------------------------------------------------------
    private Vector2[] _spawnPoints = [];
    private int _readyCount;
    private int _expectedPlayers;
    private MultiplayerSpawner _spawner;

    // -------------------------------------------------------------------------
    // HUD exports — wire in the editor
    // -------------------------------------------------------------------------
    [Export] private GameHUD    _gameHUD;
    [Export] private Texture2D  _kernelCowboyPortrait;
    [Export] private Texture2D  _sirEdwardPortrait;
    [Export] private Texture2D  _steampunkPortrait;
    [Export] private Texture2D  _vampirePortrait;

    // -------------------------------------------------------------------------
    // Countdown exports — wire in the editor
    // -------------------------------------------------------------------------
    [Export] private CanvasLayer        _countdownOverlay;
    [Export] private Label              _countdownLabel;
    [Export] private AudioStreamPlayer  _threeAudio;
    [Export] private AudioStreamPlayer  _twoAudio;
    [Export] private AudioStreamPlayer  _oneAudio;
    [Export] private AudioStreamPlayer  _fightAudio;

    /// <summary>Seconds of silence before "3" appears.</summary>
    [Export] private float _countdownInitialDelay = 1.5f;
    /// <summary>Seconds each number is shown before the next one appears.</summary>
    [Export] private float _countdownStepDuration = 1.0f;

    // -------------------------------------------------------------------------
    // Per-peer match state
    // -------------------------------------------------------------------------
    private class PeerSlot
    {
        public long   PeerId;
        public string CharName;
        public int    SpawnIndex;
        public int    StocksRemaining;
        public bool   Eliminated;
        public int    HudIndex;
    }

    private readonly List<PeerSlot> _peerSlots    = [];
    private readonly HashSet<long>  _deathHandled = [];
    private bool                    _matchOver;
    private bool                    _countdownActive;

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------
    public override void _Ready()
    {
        _spawner = GetNode<MultiplayerSpawner>("MultiplayerSpawner");
        _spawner.Spawned += OnCharacterSpawned;

        // Custom spawn function: the host calls _spawner.Spawn(data), which sends
        // the data string to every peer and runs this function on each of them.
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

    public override void _Process(double delta)
    {
        if (_matchOver || _countdownActive || _peerSlots.Count == 0) return;
        if (Multiplayer.IsServer()) PollDeaths();
        RefreshHUDHealth();
    }

    // -------------------------------------------------------------------------
    // Ready handshake — host waits for all peers before spawning
    // -------------------------------------------------------------------------
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
         TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void NotifyReady() => RegisterReady();

    private void RegisterReady()
    {
        _readyCount++;
        GD.Print($"[BattleManager] Ready {_readyCount}/{_expectedPlayers}");
        if (_readyCount < _expectedPlayers) return;

        // Sort peers so all clients see the same HUD slot order.
        var sortedPeers = NetworkManager.Instance.ConnectedPeers.OrderBy(id => id).ToList();
        int stocks = GameManager.Instance.CurrentMatch.Stocks;
        var slotParts = new List<string>();

        for (int i = 0; i < sortedPeers.Count; i++)
        {
            long   peerId     = sortedPeers[i];
            string charName   = NetworkManager.Instance.PeerCharacters.TryGetValue(peerId, out string c) ? c : "KernelCowboy";
            int    spawnIndex = _spawnPoints.Length > 0 ? i % _spawnPoints.Length : 0;

            slotParts.Add($"{peerId}:{charName}:{spawnIndex}");
            SpawnCharacter(peerId, spawnIndex);
        }

        // Broadcast slot layout + stock count to every peer (including host) so
        // each client can build _peerSlots and initialise their own HUD.
        Rpc(nameof(InitMatchSlots), string.Join("|", slotParts), stocks);
    }

    // Runs on every peer after all characters have been spawned.
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
         TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void InitMatchSlots(string slotData, int stocks)
    {
        _peerSlots.Clear();
        string[] entries = slotData.Split('|');
        for (int i = 0; i < entries.Length; i++)
        {
            string[] parts = entries[i].Split(':');
            if (parts.Length < 3) continue;
            _peerSlots.Add(new PeerSlot
            {
                PeerId          = long.Parse(parts[0]),
                CharName        = parts[1],
                SpawnIndex      = int.Parse(parts[2]),
                StocksRemaining = stocks,
                Eliminated      = false,
                HudIndex        = i,
            });
        }
        InitHUD();
        StartCountdown();
    }

    // -------------------------------------------------------------------------
    // Countdown — mirrors StageManager; runs on every peer independently
    // -------------------------------------------------------------------------
    private void StartCountdown()
    {
        _countdownActive = true;
        if (_countdownOverlay != null) _countdownOverlay.Visible = true;

        FreezeCharacters();

        float d = _countdownInitialDelay;
        float s = _countdownStepDuration;

        GetTree().CreateTimer(d).Timeout         += () => { ShowCount("3"); _threeAudio?.Play(); };
        GetTree().CreateTimer(d + s).Timeout     += () => { ShowCount("2"); _twoAudio?.Play(); };
        GetTree().CreateTimer(d + s * 2).Timeout += () => { ShowCount("1"); _oneAudio?.Play(); };
        GetTree().CreateTimer(d + s * 3).Timeout += () =>
        {
            ShowCount("FIGHT!");
            _fightAudio?.Play();
            UnfreezeCharacters();
            _countdownActive = false;
        };
        GetTree().CreateTimer(d + s * 3 + 1.0f).Timeout += () =>
        {
            if (_countdownOverlay != null) _countdownOverlay.Visible = false;
        };
    }

    private void FreezeCharacters()
    {
        foreach (PeerSlot slot in _peerSlots)
        {
            Node node = _spawner.GetNodeOrNull(slot.PeerId.ToString());
            if (node != null) node.ProcessMode = ProcessModeEnum.Disabled;
        }
    }

    private void UnfreezeCharacters()
    {
        foreach (PeerSlot slot in _peerSlots)
        {
            Node node = _spawner.GetNodeOrNull(slot.PeerId.ToString());
            if (node != null) node.ProcessMode = ProcessModeEnum.Inherit;
        }
    }

    private void ShowCount(string text)
    {
        if (_countdownLabel != null) _countdownLabel.Text = text;
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

        // Each peer wires the death zone locally so their own character can take
        // instant-kill damage when it enters the zone.
        if (stage.GetNodeOrNull("DeathZone") is Area2D deathZone)
            deathZone.BodyEntered += OnDeathZoneBodyEntered;
        else
            GD.PushWarning("[BattleManager] Stage has no 'DeathZone' Area2D — characters won't die from falling.");

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

        string spawnData = $"{peerId}:{charName}:{spawnIndex}";
        GD.Print($"[BattleManager] Spawning {charName} for peer {peerId}");
        _spawner.Spawn(spawnData);
    }

    // Runs on every peer (host and all clients) when Spawn() is called.
    private Node CustomSpawn(Variant data)
    {
        string[] parts = data.AsString().Split(':');
        if (parts.Length < 3)
        {
            GD.PushError($"[BattleManager] Malformed spawn data: {data}");
            return null;
        }

        long   peerId    = long.Parse(parts[0]);
        string charName  = parts[1];
        int    spawnIndex = int.Parse(parts[2]);

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
    // Death zone — only apply damage to the locally-authoritative character so
    // we don't double-apply on the server for peer-owned characters.
    // -------------------------------------------------------------------------
    private void OnDeathZoneBodyEntered(Node2D body)
    {
        if (body is CharacterBase cb && cb.IsMultiplayerAuthority())
            cb.TakeDamage(9999);
    }

    // -------------------------------------------------------------------------
    // Death polling — server only
    // -------------------------------------------------------------------------

    // CharacterBase.IsDead is only set on the authority peer; the server sees other
    // characters' state via SyncState which syncs CurrentState.  Use Dead state as
    // the death signal instead of IsDead so it works for all peers.
    private void PollDeaths()
    {
        foreach (PeerSlot slot in _peerSlots)
        {
            if (slot.Eliminated || _deathHandled.Contains(slot.PeerId)) continue;

            var character = _spawner.GetNodeOrNull<CharacterBase>(slot.PeerId.ToString());
            if (character == null || character.CurrentState != CharacterBase.CharacterState.Dead) continue;

            _deathHandled.Add(slot.PeerId);
            HandleDeath(slot);
        }
    }

    private void HandleDeath(PeerSlot slot)
    {
        slot.StocksRemaining--;
        GD.Print($"[BattleManager] Peer {slot.PeerId} died. Stocks left: {slot.StocksRemaining}");
        Rpc(nameof(SyncStocks), slot.PeerId, slot.StocksRemaining);

        if (slot.StocksRemaining <= 0)
        {
            slot.Eliminated = true;
            _spawner.GetNodeOrNull(slot.PeerId.ToString())?.QueueFree();
            CheckMatchOver();
        }
        else
        {
            Node dying = _spawner.GetNodeOrNull(slot.PeerId.ToString());
            GetTree().CreateTimer(2.0).Timeout += () =>
            {
                _deathHandled.Remove(slot.PeerId);
                dying?.QueueFree();
                // Defer one frame so the spawner sends its despawn message before the respawn message.
                // QueueFree is asynchronous; without CallDeferred the spawn arrives on clients
                // before the despawn, causing on_spawn_receive "has_node(name)" errors.
                Callable.From(() => SpawnCharacter(slot.PeerId, slot.SpawnIndex)).CallDeferred();
            };
        }
    }

    // Syncs stock count to all peers so every client's HUD is accurate.
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
         TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SyncStocks(long peerId, int stocks)
    {
        PeerSlot slot = _peerSlots.Find(s => s.PeerId == peerId);
        if (slot == null) return;
        slot.StocksRemaining = stocks;
        if (stocks <= 0) slot.Eliminated = true;
        _gameHUD?.UpdateStocks(slot.HudIndex, stocks);
    }

    // -------------------------------------------------------------------------
    // Match over
    // -------------------------------------------------------------------------
    private void CheckMatchOver()
    {
        var survivors = _peerSlots.Where(s => !s.Eliminated).ToList();
        if (survivors.Count > 1) return;

        _matchOver = true;
        long winnerPeerId = survivors.Count == 1 ? survivors[0].PeerId : -1;
        Rpc(nameof(NotifyMatchOver), winnerPeerId);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
         TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void NotifyMatchOver(long winnerPeerId)
    {
        _matchOver = true;
        GD.Print($"[BattleManager] Match over! Winner: peer {winnerPeerId}");
        // TODO: navigate to results / main menu
    }

    // -------------------------------------------------------------------------
    // HUD
    // -------------------------------------------------------------------------
    private void InitHUD()
    {
        if (_gameHUD == null) return;

        long localId = Multiplayer.GetUniqueId();
        var infos = new GameHUD.PlayerInfo[_peerSlots.Count];

        for (int i = 0; i < _peerSlots.Count; i++)
        {
            PeerSlot slot = _peerSlots[i];
            infos[i] = new GameHUD.PlayerInfo
            {
                Name    = slot.PeerId == localId ? "You" : $"P{i + 1}",
                Portrait = PortraitFor(slot.CharName),
                Health   = 100f,
                Stocks   = slot.StocksRemaining,
            };
        }

        _gameHUD.InitializePlayers(infos);
    }

    private void RefreshHUDHealth()
    {
        if (_gameHUD == null) return;
        foreach (PeerSlot slot in _peerSlots)
        {
            if (slot.Eliminated) continue;
            var character = _spawner.GetNodeOrNull<CharacterBase>(slot.PeerId.ToString());
            if (character == null) continue;
            _gameHUD.UpdateHealth(slot.HudIndex, character.CurrentHP);
        }
    }

    private Texture2D PortraitFor(string charName) => charName switch
    {
        "KernelCowboy" => _kernelCowboyPortrait,
        "SirEdward"    => _sirEdwardPortrait,
        "Steampunk"    => _steampunkPortrait,
        "Vampire"      => _vampirePortrait,
        _              => null,
    };

    // -------------------------------------------------------------------------
    // Peer disconnect during match
    // -------------------------------------------------------------------------
    private void OnPeerLeft(long id)
    {
        if (!Multiplayer.IsServer()) return;

        Node character = _spawner.GetNodeOrNull(id.ToString());
        if (character != null)
        {
            character.QueueFree();
            GD.Print($"[BattleManager] Removed character for disconnected peer {id}");
        }

        // Treat disconnect as elimination so the match can end normally.
        PeerSlot slot = _peerSlots.Find(s => s.PeerId == id);
        if (slot != null && !slot.Eliminated)
        {
            slot.Eliminated = true;
            Rpc(nameof(SyncStocks), id, 0);
            CheckMatchOver();
        }
    }
}
