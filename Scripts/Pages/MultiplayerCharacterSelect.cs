using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Multiplayer character select. Attach to the root Control of MultiplayerCharacterSelect.tscn.
///
/// SCENE SETUP:
///   - 4 character buttons (KernelCowboy, SirEdward, Steampunk, Vampire)
///   - StartMatchButton  — visible to host only; enabled once every peer has a selection
///   - BackButton
///   - StocksLabel + StocksUpButton + StocksDownButton  — host adjusts; syncs to all
///   - SlotsContainer    — HBoxContainer; PlayerSlot scenes are instantiated here at runtime
///   - PlayerSlotScene   — drag PlayerSlot.tscn into this export slot
///   - One Texture2D export per character for portraits (assign in Inspector)
///
/// FLOW:
///   Clicking a character button immediately RPC's the selection to all peers.
///   Everyone's PlayerSlot box updates in real time as players browse.
///   Once every peer has selected something the host's Start Match button activates.
///   Host clicks Start Match → all clients change scene simultaneously.
///
/// DEPENDENCIES:
///   NetworkManager.Instance.ConnectedPeers / PeerCharacters must be populated
///   before this scene loads (done by the join/host flow).
/// </summary>
public partial class MultiplayerCharacterSelect : Control
{
    // ── Exports ───────────────────────────────────────────────────────────────

    [Export] private Button _character1Button;
    [Export] private Button _character2Button;
    [Export] private Button _character3Button;
    [Export] private Button _character4Button;

    [Export] private Button _backButton;
    [Export] private Button _startMatchButton;

    [Export] private Label  _stocksLabel;
    [Export] private Button _stocksUpButton;
    [Export] private Button _stocksDownButton;

    /// <summary>HBoxContainer that holds the dynamically created PlayerSlot nodes.</summary>
    [Export] private Control _slotsContainer;

    /// <summary>Drag PlayerSlot.tscn here in the Inspector.</summary>
    [Export] private PackedScene _playerSlotScene;

    // Character portrait textures — assign each in the Inspector.
    [Export] private Texture2D _kernelCowboyPortrait;
    [Export] private Texture2D _sirEdwardPortrait;
    [Export] private Texture2D _steampunkPortrait;
    [Export] private Texture2D _vampirePortrait;

    // ── Visuals ───────────────────────────────────────────────────────────────

    private static readonly Color SelectedTint   = new Color(1.0f, 0.85f, 0.2f);
    private static readonly Color UnselectedTint = new Color(0.55f, 0.55f, 0.55f);

    // ── Private state ─────────────────────────────────────────────────────────

    private int _stocks = 1;

    private GameManager.CharacterType? _selectedCharacter;
    private Button                     _selectedCharacterButton;

    private List<long>                        _peerOrder      = new();
    private readonly Dictionary<long, string>     _peerSelections = new();
    private readonly Dictionary<long, PlayerSlot> _slotNodes      = new();

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        _character1Button.Pressed += () => SelectCharacter(_character1Button, GameManager.CharacterType.KernelCowboy);
        _character2Button.Pressed += () => SelectCharacter(_character2Button, GameManager.CharacterType.SirEdward);
        _character3Button.Pressed += () => SelectCharacter(_character3Button, GameManager.CharacterType.Steampunk);
        _character4Button.Pressed += () => SelectCharacter(_character4Button, GameManager.CharacterType.Vampire);

        _backButton.Pressed += OnBackPressed;

        _startMatchButton.Pressed  += OnStartMatchPressed;
        _startMatchButton.Visible   = Multiplayer.IsServer();
        _startMatchButton.Disabled  = true;

        bool isHost = Multiplayer.IsServer();
        _stocksUpButton.Disabled   = !isHost;
        _stocksDownButton.Disabled = !isHost;
        if (isHost)
        {
            _stocksUpButton.Pressed   += () => Rpc(nameof(SyncStocks), Mathf.Clamp(_stocks + 1, 1, 5));
            _stocksDownButton.Pressed += () => Rpc(nameof(SyncStocks), Mathf.Clamp(_stocks - 1, 1, 5));
        }

        UpdateStocksLabel();
        UpdateCharacterButtonStates();

        _character1Button.GrabFocus();

        if (isHost)
        {
            // ConnectedPeers already includes the host (peer 1) — sort so the host
            // is always slot 0 / "Player 1", matching BattleManager's ordering.
            var sorted = NetworkManager.Instance.ConnectedPeers.OrderBy(id => id);
            Rpc(nameof(ReceivePeerList), string.Join(",", sorted));
        }
    }

    // ── Character selection ───────────────────────────────────────────────────

    private void SelectCharacter(Button button, GameManager.CharacterType character)
    {
        _selectedCharacterButton = button;
        _selectedCharacter       = character;

        UpdateCharacterButtonStates();

        string characterName = character.ToString();

        if (Multiplayer.IsServer())
        {
            NetworkManager.Instance.PeerCharacters[(long)Multiplayer.GetUniqueId()] = characterName;
            Rpc(nameof(ClientReceiveSelection), (long)Multiplayer.GetUniqueId(), characterName);
        }
        else
        {
            RpcId(1, nameof(ServerReceiveSelection), characterName);
        }
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    private void OnBackPressed() => GameManager.Instance.GoToMainMenu();

    private void OnStartMatchPressed()
    {
        if (!Multiplayer.IsServer()) return;
        MusicManager.Instance?.StopMusic();
        GameManager.Instance.SetStocks(_stocks);
        NetworkManager.Instance.StartBattle();
    }

    // ── RPCs ──────────────────────────────────────────────────────────────────

    /// <summary>Host broadcasts ordered peer ID list so all clients know how many players there are.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceivePeerList(string peerIdsStr)
    {
        _peerOrder.Clear();
        if (!string.IsNullOrEmpty(peerIdsStr))
            foreach (string s in peerIdsStr.Split(','))
                if (long.TryParse(s, out long id))
                    _peerOrder.Add(id);

        BuildSlotNodes();
    }

    /// <summary>Client sends their current character selection to the server.</summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ServerReceiveSelection(string characterName)
    {
        long senderId = Multiplayer.GetRemoteSenderId();
        NetworkManager.Instance.PeerCharacters[senderId] = characterName;
        Rpc(nameof(ClientReceiveSelection), senderId, characterName);
    }

    /// <summary>Broadcasts a peer's current selection to all clients so their slot box updates in real time.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ClientReceiveSelection(long peerId, string characterName)
    {
        _peerSelections[peerId] = characterName;

        if (_slotNodes.TryGetValue(peerId, out PlayerSlot slot))
            slot.ShowCharacter(FriendlyName(characterName), PortraitFor(characterName));

        UpdateCharacterButtonStates();

        if (Multiplayer.IsServer())
            CheckAllReady();
    }

    /// <summary>Host syncs the stock count to all clients.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SyncStocks(int stocks)
    {
        _stocks = stocks;
        UpdateStocksLabel();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private void CheckAllReady()
    {
        bool allReady = _peerOrder.Count > 0 && _peerOrder.All(id => _peerSelections.ContainsKey(id));
        _startMatchButton.Disabled = !allReady;
    }

    private void BuildSlotNodes()
    {
        foreach (Node child in _slotsContainer.GetChildren())
            child.QueueFree();
        _slotNodes.Clear();

        long localId = (long)Multiplayer.GetUniqueId();

        for (int i = 0; i < _peerOrder.Count; i++)
        {
            long       peerId = _peerOrder[i];
            PlayerSlot slot   = _playerSlotScene.Instantiate<PlayerSlot>();

            _slotsContainer.AddChild(slot);
            slot.Init(i + 1, peerId == localId);
            slot.ShowWaiting();

            _slotNodes[peerId] = slot;
        }

        GD.Print($"[MultiplayerCharacterSelect] Built {_peerOrder.Count} player slot(s).");
        CallDeferred(nameof(PositionSlots));
    }

    private void PositionSlots()
    {
        if (_slotNodes.Count == 0) return;

        float containerW = _slotsContainer.Size.X;
        float containerH = _slotsContainer.Size.Y;
        float sectionW   = containerW / 4f;

        int i = 0;
        foreach (long peerId in _peerOrder)
        {
            if (!_slotNodes.TryGetValue(peerId, out PlayerSlot slot)) { i++; continue; }

            Vector2 size = slot.Size;
            float   x    = i * sectionW + (sectionW - size.X) / 2f;
            float   y    = (containerH - size.Y) / 2f;

            slot.Position = new Vector2(x, Mathf.Max(y, 0f));
            i++;
        }
    }

    private static string FriendlyName(string enumName) => enumName switch
    {
        "KernelCowboy" => "Kernel Cowboy",
        "SirEdward"    => "Sir Edward",
        "Steampunk"    => "Steampunk",
        "Vampire"      => "Vampire",
        _              => enumName
    };

    private Texture2D PortraitFor(string enumName) => enumName switch
    {
        "KernelCowboy" => _kernelCowboyPortrait,
        "SirEdward"    => _sirEdwardPortrait,
        "Steampunk"    => _steampunkPortrait,
        "Vampire"      => _vampirePortrait,
        _              => null
    };

    private void UpdateStocksLabel() => _stocksLabel.Text = $"Stocks: {_stocks}";

    private void UpdateCharacterButtonStates()
    {
        long localId = (long)Multiplayer.GetUniqueId();

        var takenByOthers = new System.Collections.Generic.HashSet<string>();
        foreach (var (peerId, charName) in _peerSelections)
            if (peerId != localId)
                takenByOthers.Add(charName);

        (Button btn, GameManager.CharacterType ch)[] map =
        [
            (_character1Button, GameManager.CharacterType.KernelCowboy),
            (_character2Button, GameManager.CharacterType.SirEdward),
            (_character3Button, GameManager.CharacterType.Steampunk),
            (_character4Button, GameManager.CharacterType.Vampire),
        ];

        foreach (var (btn, ch) in map)
        {
            if (btn == null) continue;

            bool taken    = takenByOthers.Contains(ch.ToString());
            bool selected = btn == _selectedCharacterButton;

            btn.Disabled = taken;
            btn.Modulate = selected ? SelectedTint : (taken ? UnselectedTint : Colors.White);
        }
    }
}