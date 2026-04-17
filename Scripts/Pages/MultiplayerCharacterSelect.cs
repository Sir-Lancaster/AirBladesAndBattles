using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Multiplayer character select. Attach to the root Control of MultiplayerCharacterSelect.tscn.
///
/// SCENE SETUP:
///   - 4 character buttons (KernelCowboy, SirEdward, Steampunk, Vampire)
///   - ConfirmButton   — local player locks in their pick
///   - StartMatchButton — visible to host only; enabled when all peers have confirmed
///   - BackButton
///   - StocksLabel + StocksUpButton + StocksDownButton  — only host can adjust; syncs to all
///   - Player1StatusLabel … Player4StatusLabel           — one per connected peer slot
///
/// FLOW:
///   Each player (on their own machine) picks a character and hits Confirm.
///   The choice is sent via RPC to the host, which broadcasts readiness to everyone.
///   Once all players have confirmed, the host's Start Match button activates.
///   Host clicks Start Match → all clients change to the battle scene simultaneously.
///
/// DEPENDENCIES:
///   NetworkManager.Instance.IsHost / ConnectedPeers / PeerCharacters must be set
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
    [Export] private Button _confirmButton;
    [Export] private Button _startMatchButton;

    [Export] private Label  _titleLabel;
    [Export] private Label  _stocksLabel;
    [Export] private Button _stocksUpButton;
    [Export] private Button _stocksDownButton;

    [Export] private Label _player1StatusLabel;
    [Export] private Label _player2StatusLabel;
    [Export] private Label _player3StatusLabel;
    [Export] private Label _player4StatusLabel;

    // ── Visuals ───────────────────────────────────────────────────────────────

    private static readonly Color SelectedTint   = new Color(1.0f, 0.85f, 0.2f);
    private static readonly Color UnselectedTint = new Color(0.55f, 0.55f, 0.55f);
    private static readonly Color ReadyTint      = new Color(0.2f,  1.0f,  0.4f);

    // ── Private state ─────────────────────────────────────────────────────────

    private int    _stocks = 1;
    private bool   _hasConfirmed;

    private GameManager.CharacterType? _selectedCharacter;
    private Button                     _selectedCharacterButton;

    private List<long>                      _peerOrder      = new();
    private readonly Dictionary<long, string> _confirmedPeers = new();

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        _character1Button.Pressed += () => SelectCharacter(_character1Button, GameManager.CharacterType.KernelCowboy);
        _character2Button.Pressed += () => SelectCharacter(_character2Button, GameManager.CharacterType.SirEdward);
        _character3Button.Pressed += () => SelectCharacter(_character3Button, GameManager.CharacterType.Steampunk);
        _character4Button.Pressed += () => SelectCharacter(_character4Button, GameManager.CharacterType.Vampire);

        _backButton.Pressed    += OnBackPressed;
        _confirmButton.Pressed += OnConfirmPressed;
        _confirmButton.Disabled = true;

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

        _titleLabel.Text = "Pick Your Character";

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
        if (_hasConfirmed) return;

        _selectedCharacterButton = button;
        _selectedCharacter       = character;
        _confirmButton.Disabled  = false;

        UpdateCharacterButtonVisuals();
    }

    private void OnConfirmPressed()
    {
        if (_selectedCharacter == null || _hasConfirmed) return;

        _hasConfirmed           = true;
        _confirmButton.Disabled = true;
        SetCharacterButtonsDisabled(true);

        string characterName = _selectedCharacter.Value.ToString();

        if (Multiplayer.IsServer())
        {
            NetworkManager.Instance.PeerCharacters[(long)Multiplayer.GetUniqueId()] = characterName;
            Rpc(nameof(ClientPeerReady), (long)Multiplayer.GetUniqueId(), characterName);
        }
        else
        {
            RpcId(1, nameof(ServerReceiveChoice), characterName);
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

        UpdateStatusLabels();
    }

    /// <summary>Client sends their confirmed character to the server.</summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ServerReceiveChoice(string characterName)
    {
        long senderId = Multiplayer.GetRemoteSenderId();
        NetworkManager.Instance.PeerCharacters[senderId] = characterName;
        Rpc(nameof(ClientPeerReady), senderId, characterName);
    }

    /// <summary>Server notifies all clients that a peer has locked in their character.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ClientPeerReady(long peerId, string characterName)
    {
        _confirmedPeers[peerId] = characterName;
        UpdateStatusLabels();

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
        bool allReady = _peerOrder.Count > 0 && _peerOrder.All(id => _confirmedPeers.ContainsKey(id));
        _startMatchButton.Disabled = !allReady;
    }

    private void UpdateStatusLabels()
    {
        Label[] labels = { _player1StatusLabel, _player2StatusLabel, _player3StatusLabel, _player4StatusLabel };

        for (int i = 0; i < labels.Length; i++)
        {
            if (labels[i] == null) continue;

            if (i >= _peerOrder.Count)
            {
                labels[i].Visible = false;
                continue;
            }

            labels[i].Visible = true;

            long   peerId    = _peerOrder[i];
            bool   isLocal   = peerId == (long)Multiplayer.GetUniqueId();
            string playerTag = $"Player {i + 1}{(isLocal ? " (You)" : "")}";

            if (_confirmedPeers.TryGetValue(peerId, out string characterName))
            {
                labels[i].Text     = $"{playerTag}: {characterName}";
                labels[i].Modulate = ReadyTint;
            }
            else
            {
                labels[i].Text     = $"{playerTag}: Waiting...";
                labels[i].Modulate = Colors.White;
            }
        }
    }

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

    private void SetCharacterButtonsDisabled(bool disabled)
    {
        _character1Button.Disabled = disabled;
        _character2Button.Disabled = disabled;
        _character3Button.Disabled = disabled;
        _character4Button.Disabled = disabled;
    }
}
