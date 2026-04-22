using Godot;
using System.Collections.Generic;

/// <summary>
/// Attach to the root Node2D of Game.tscn (single-player game scene).
///
/// SCENE SETUP:
///   Game (Node2D) — this script
///     GameHUD (CanvasLayer) — drag into _gameHUD export
///
/// FLOW:
///   _Ready loads the stage from GameManager.CurrentMatch.StageName,
///   spawns one character per slot (player slot 0, AI slots 1+),
///   then polls for deaths each frame. On death: decrement stocks,
///   respawn after 2 s if stocks remain, eliminate if out. Last
///   survivor triggers GameManager.OnMatchOver(winnerIndex).
///
/// DEPENDENCIES:
///   GameManager.CurrentMatch must be fully configured (Slots, StageName, Stocks)
///   before this scene loads.
/// </summary>
public partial class StageManager : Node2D
{
    // ── Scene paths ───────────────────────────────────────────────────────────

    private static readonly Dictionary<string, string> PlayerScenePaths = new()
    {
        { "KernelCowboy", "res://Scenes/KernelCowboy/KernelCowboy.tscn" },
        { "SirEdward",    "res://Scenes/Edward/Edward.tscn"              },
        { "Steampunk",    "res://Scenes/Steampunk/Steampunk.tscn"        },
        { "Vampire",      "res://Scenes/Vampire/Vampire.tscn"            },
    };

    private static readonly Dictionary<string, string> AiScenePaths = new()
    {
        { "KernelCowboy", "res://Scenes/KernelCowboy/AiKernelCowboy.tscn"},
        { "SirEdward", "res://Scenes/Edward/EvilEdward.tscn"},
        { "Steampunk", "res://Scenes/Steampunk/AiSteampunk.tscn" },
        { "Vampire", "res://Scenes/Vampire/VampireAi.tscn"}
    };

    // ── Exports ───────────────────────────────────────────────────────────────

    [Export] private GameHUD _gameHUD;

    // Countdown UI — add a CanvasLayer > Label in Game.tscn and wire these up.
    [Export] private CanvasLayer       _countdownOverlay;
    [Export] private Label             _countdownLabel;
    [Export] private AudioStreamPlayer _tickAudio;   // plays on 3, 2, 1
    [Export] private AudioStreamPlayer _fightAudio;  // plays on FIGHT!

    [Export] private Texture2D _kernelCowboyPortrait;
    [Export] private Texture2D _sirEdwardPortrait;
    [Export] private Texture2D _steampunkPortrait;
    [Export] private Texture2D _vampirePortrait;

    // ── Private state ─────────────────────────────────────────────────────────

    private class SlotData
    {
        public GameManager.PlayerSlot Config;
        public int   StocksRemaining;
        public Node  ActiveCharacter;
        public bool  Eliminated;
        public int   SpawnIndex;
    }

    private SlotData[]     _slots           = [];
    private Vector2[]      _spawnPoints     = [];
    private HashSet<Node>  _deathHandled    = new();
    private bool           _matchOver;
    private bool           _countdownActive = true;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        LoadStage();
        InitSlots();
        SpawnAllCharacters();
        InitHUD();
        StartCountdown();
    }

    public override void _Process(double delta)
    {
        if (_matchOver || _countdownActive) return;
        PollDeaths();
        RefreshHUDHealth();
    }

    // ── Countdown ─────────────────────────────────────────────────────────────

    private void StartCountdown()
    {
        if (_countdownOverlay != null) _countdownOverlay.Visible = true;

        FreezeCharacters();

        ShowCount("3");
        _tickAudio?.Play();

        GetTree().CreateTimer(1.5).Timeout += () =>
        {
            ShowCount("2");
            _tickAudio?.Play();
        };
        GetTree().CreateTimer(3.0).Timeout += () =>
        {
            ShowCount("1");
            _tickAudio?.Play();
        };
        GetTree().CreateTimer(4.5).Timeout += () =>
        {
            ShowCount("FIGHT!");
            _fightAudio?.Play();
            UnfreezeCharacters();
            _countdownActive = false;
        };
        GetTree().CreateTimer(5.5).Timeout += () =>
        {
            if (_countdownOverlay != null) _countdownOverlay.Visible = false;
        };
    }

    private void FreezeCharacters()
    {
        foreach (SlotData slot in _slots)
        {
            if (slot.ActiveCharacter != null)
                slot.ActiveCharacter.ProcessMode = ProcessModeEnum.Disabled;
        }
    }

    private void UnfreezeCharacters()
    {
        foreach (SlotData slot in _slots)
        {
            if (slot.ActiveCharacter != null)
                slot.ActiveCharacter.ProcessMode = ProcessModeEnum.Inherit;
        }
    }

    private void ShowCount(string text)
    {
        if (_countdownLabel != null) _countdownLabel.Text = text;
    }

    // ── Stage loading ─────────────────────────────────────────────────────────

    private void LoadStage()
    {
        string path = GameManager.Instance.CurrentMatch.StageName;
        if (string.IsNullOrEmpty(path))
        {
            GD.PushWarning("[StageManager] No stage path in GameManager.CurrentMatch.");
            return;
        }

        var stageScene = GD.Load<PackedScene>(path);
        if (stageScene == null) { GD.PushError($"[StageManager] Could not load stage: {path}"); return; }

        Node stage = stageScene.Instantiate();
        AddChild(stage);

        // Wire kill zone — stage places the geometry, StageManager owns the logic.
        if (stage.GetNodeOrNull("DeathZone") is Area2D deathZone)
            deathZone.BodyEntered += OnDeathZoneBodyEntered;
        else
            GD.PushWarning("[StageManager] Stage has no 'DeathZone' Area2D — characters won't die from falling.");

        Node spawnRoot = stage.GetNodeOrNull("SpawnPoints");
        if (spawnRoot == null)
        {
            GD.PushWarning("[StageManager] Stage has no 'SpawnPoints' node.");
            return;
        }

        var points = new List<Vector2>();
        for (int i = 1; i <= 4; i++)
        {
            var marker = spawnRoot.GetNodeOrNull<Marker2D>($"Spawn{i}");
            if (marker != null) points.Add(marker.GlobalPosition);
        }
        _spawnPoints = [..points];
    }

    // ── Slot initialisation ───────────────────────────────────────────────────

    private void InitSlots()
    {
        var match = GameManager.Instance.CurrentMatch;
        _slots = new SlotData[match.Slots.Length];
        for (int i = 0; i < match.Slots.Length; i++)
        {
            _slots[i] = new SlotData
            {
                Config          = match.Slots[i],
                StocksRemaining = match.Stocks,
                SpawnIndex      = i,
            };
        }
    }

    // ── Spawning ──────────────────────────────────────────────────────────────

    private void SpawnAllCharacters()
    {
        for (int i = 0; i < _slots.Length; i++)
            SpawnCharacter(i);

    }

    private void SpawnCharacter(int slotIndex)
    {
        SlotData slot    = _slots[slotIndex];
        string   charKey = slot.Config.Character.ToString();
        bool     isAi    = slot.Config.IsAI;

        string path;
        if (isAi && AiScenePaths.TryGetValue(charKey, out string aiPath))
            path = aiPath;
        else if (!PlayerScenePaths.TryGetValue(charKey, out path))
        {
            GD.PushError($"[StageManager] Unknown character: {charKey}");
            return;
        }

        var scene = GD.Load<PackedScene>(path);
        if (scene == null) { GD.PushError($"[StageManager] Could not load: {path}"); return; }

        Node character  = scene.Instantiate();
        character.Name  = $"Slot{slotIndex}";
        character.AddToGroup("characters");

        if (_spawnPoints.Length > 0 && character is Node2D n2d)
            n2d.GlobalPosition = _spawnPoints[slot.SpawnIndex % _spawnPoints.Length];

        AddChild(character);
        slot.ActiveCharacter = character;

        GD.Print($"[StageManager] Spawned {charKey} (slot {slotIndex}, AI={isAi}).");
    }

    // ── Death polling ─────────────────────────────────────────────────────────

    private void PollDeaths()
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            SlotData slot = _slots[i];
            if (slot.Eliminated || slot.ActiveCharacter == null) continue;
            if (_deathHandled.Contains(slot.ActiveCharacter)) continue;

            bool dead = (slot.ActiveCharacter is CharacterBase cb && cb.IsDead)
                     || (slot.ActiveCharacter is AiBaseClass ai  && ai.IsDead);

            if (!dead) continue;

            _deathHandled.Add(slot.ActiveCharacter);
            HandleDeath(i);
        }
    }

    private void OnDeathZoneBodyEntered(Node2D body)
    {
        if (body is CharacterBase cb)
        {
            GD.Print($"[DeathZone] CharacterBase '{body.Name}' entered kill zone.");
            cb.TakeDamage(9999);
            return;
        }
        if (body is AiBaseClass ai)
        {
            GD.Print($"[DeathZone] AiBaseClass '{body.Name}' entered kill zone.");
            ai.TakeDamage(9999);
        }
    }

    private void HandleDeath(int slotIndex)
    {
        SlotData slot = _slots[slotIndex];
        slot.StocksRemaining--;
        GD.Print($"[StageManager] Slot {slotIndex} died. Stocks left: {slot.StocksRemaining}");
        _gameHUD?.UpdateStocks(slotIndex, slot.StocksRemaining);

        if (slot.StocksRemaining <= 0)
        {
            slot.Eliminated = true;
            slot.ActiveCharacter?.QueueFree();
            slot.ActiveCharacter = null;
            _gameHUD?.UpdateHealth(slotIndex, 0);
            GD.Print($"[StageManager] Slot {slotIndex} eliminated.");
            CheckMatchOver();
        }
        else
        {
            Node dying = slot.ActiveCharacter;
            GetTree().CreateTimer(2.0).Timeout += () =>
            {
                _deathHandled.Remove(dying);
                dying?.QueueFree();
                SpawnCharacter(slotIndex);
                CallDeferred(nameof(WireAiTargets));
                // Defer by one frame so the new character's _Ready runs first
                // and CurrentHP is fully initialised before we read MaxHP.
                CallDeferred(nameof(RefreshRespawnHUD), slotIndex);
            };
        }
    }

    // ── Match over ────────────────────────────────────────────────────────────

    private void CheckMatchOver()
    {
        int survivorIndex = -1;
        int survivorCount = 0;

        for (int i = 0; i < _slots.Length; i++)
        {
            if (!_slots[i].Eliminated)
            {
                survivorCount++;
                survivorIndex = i;
            }
        }

        if (survivorCount > 1) return;

        _matchOver = true;
        GD.Print($"[StageManager] Match over. Winner: slot {survivorIndex}");
        GameManager.Instance.OnMatchOver(survivorIndex);
    }

    // ── HUD ───────────────────────────────────────────────────────────────────

    private void InitHUD()
    {
        if (_gameHUD == null) return;

        var match = GameManager.Instance.CurrentMatch;
        var infos = new GameHUD.PlayerInfo[_slots.Length];

        for (int i = 0; i < _slots.Length; i++)
        {
            string charKey = _slots[i].Config.Character.ToString();
            infos[i] = new GameHUD.PlayerInfo
            {
                Name    = i == 0 ? "Player 1" : $"AI {i}",
                Portrait = PortraitFor(charKey),
                Health   = GetMaxHp(i),
                Stocks   = match.Stocks,
            };
        }

        _gameHUD.InitializePlayers(infos);
    }

    private void RefreshRespawnHUD(int slotIndex)
    {
        _gameHUD?.UpdateHealth(slotIndex, GetMaxHp(slotIndex));
        GD.Print($"[StageManager] Slot {slotIndex} respawned — HUD reset to {GetMaxHp(slotIndex)} HP.");
    }

    private void RefreshHUDHealth()
    {
        if (_gameHUD == null) return;
        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i].Eliminated || _slots[i].ActiveCharacter == null) continue;
            _gameHUD.UpdateHealth(i, GetCurrentHp(i));
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private float GetCurrentHp(int slotIndex)
    {
        Node c = _slots[slotIndex].ActiveCharacter;
        if (c is CharacterBase cb) return cb.CurrentHP;
        if (c is AiBaseClass ai)   return ai.CurrentHP;
        return 0f;
    }

    private float GetMaxHp(int slotIndex)
    {
        Node c = _slots[slotIndex].ActiveCharacter;
        if (c is CharacterBase cb) return cb.MaxHP;
        if (c is AiBaseClass ai)   return ai.MaxHP;
        return 100f;
    }

    private Texture2D PortraitFor(string charKey) => charKey switch
    {
        "KernelCowboy" => _kernelCowboyPortrait,
        "SirEdward"    => _sirEdwardPortrait,
        "Steampunk"    => _steampunkPortrait,
        "Vampire"      => _vampirePortrait,
        _              => null,
    };
}