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
        UpdateCharacterButtonVisuals();

        if (isHost)
        {
            var peers = new List<long> { (long)Multiplayer.GetUniqueId() };
            foreach (long id in NetworkManager.Instance.ConnectedPeers)
                peers.Add(id);

            Rpc(nameof(ReceivePeerList), string.Join(",", peers));
        }
    }

    // ── Character selection ───────────────────────────────────────────────────

    private void SelectCharacter(Button button, GameManager.CharacterType character)
    {
        _selectedCharacterButton = button;
        _selectedCharacter       = character;

        UpdateCharacterButtonVisuals();

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

    private void OnBackPressed()       => GameManager.Instance.GoToMainMenu();
    private void OnStartMatchPressed() => Rpc(nameof(AllStartBattle));

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

    /// <summary>Host tells all peers to load the battle scene.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void AllStartBattle()
    {
        GameManager.Instance.SetStocks(_stocks);
        GameManager.Instance.StartMultiplayerMatch();
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

        // Center slots and scale spacing so fewer players spread out more.
        if (_slotsContainer is HBoxContainer hbox)
        {
            hbox.Alignment = BoxContainer.AlignmentMode.Center;
            int separation = _peerOrder.Count switch
            {
                1 => 0,
                2 => 120,
                3 => 60,
                _ => 30
            };
            hbox.AddThemeConstantOverride("separation", separation);
        }

        GD.Print($"[MultiplayerCharacterSelect] Built {_peerOrder.Count} player slot(s).");
    }

    private string FriendlyName(string enumName) => enumName switch
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

    private void UpdateCharacterButtonVisuals()
    {
        Button[] buttons = { _character1Button, _character2Button, _character3Button, _character4Button };
        foreach (Button btn in buttons)
        {
            if (btn == null) continue;
            btn.Modulate = btn == _selectedCharacterButton ? SelectedTint : UnselectedTint;
        }
    }
}