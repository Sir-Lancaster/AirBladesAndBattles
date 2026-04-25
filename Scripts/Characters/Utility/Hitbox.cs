using System.Collections.Generic;
using Godot;

public interface IDamageable
{
    bool TryReceiveHit(Node attacker, Hitbox hitbox, int damage);
}

/// <summary>
/// Transient damage area used by attacks/specials.
/// Detects overlap targets and delegates hit-validation via IDamageable.
/// </summary>
public partial class Hitbox : Area2D
{
    /// <summary>Damage value passed to the target when a hit is accepted.</summary>
    [Export] public int Damage = 4;

    /// <summary>Lifetime in seconds before this hitbox is auto-freed.</summary>
    [Export] public float Lifetime = 0.12f;

    /// <summary>If true, each target can only be hit once by this hitbox instance.</summary>
    [Export] public bool OneHitPerTarget = true;

    /// <summary>If true, this hitbox is destroyed after its first successful hit.</summary>
    [Export] public bool DestroyOnFirstHit = false;

    /// <summary>The node that created/owns this hitbox (usually the attacker).</summary>
    public Node OwnerNode { get; private set; }

    /// <summary>Fired each time this hitbox successfully lands a hit.</summary>
    public event System.Action HitLanded;

    // Tracks targets already hit by this hitbox when OneHitPerTarget is enabled.
    private readonly HashSet<ulong> _hitTargetIds = new();

    // Prevents starting multiple lifetime timers.
    private bool _lifetimeCountdownStarted;

    /// <summary>
    /// Wires overlap callbacks and starts the lifetime countdown.
    /// </summary>

    public override void _Ready()
    {
        // Detect characters on layer 2 (characters never collide with each other body-to-body,
        // but hitboxes as Area2D still need to see them via their collision layer).
        CollisionMask = 2;

        AreaEntered += OnAreaEntered;
        BodyEntered += OnBodyEntered;
    }

    /// <summary>Updates the damage value mid-lifetime (e.g. for held attacks).</summary>
    public void UpdateDamage(int damage) => Damage = damage;

    /// <summary>
    /// Initializes owner, damage, and optional lifetime override.
    /// </summary>
    /// <param name="ownerNode">Attacker node that spawned this hitbox.</param>
    /// <param name="damage">Damage to attempt on valid targets.</param>
    /// <param name="lifetimeSeconds">Optional lifetime override. Negative keeps current value.</param>
    public void Activate(Node ownerNode, int damage, float lifetimeSeconds = -1f)
    {
        if (ownerNode == null) return;

        OwnerNode = ownerNode;
        Damage = damage;

        if (lifetimeSeconds >= 0f)
            Lifetime = lifetimeSeconds;

        EnsureLifetimeCountdown();
    }

    /// <summary>
    /// Starts the one-shot lifetime timer if not already running.
    /// </summary>
    private void EnsureLifetimeCountdown()
    {
        if (_lifetimeCountdownStarted || Lifetime <= 0f)
            return;

        _lifetimeCountdownStarted = true;
        StartLifetimeCountdown(Lifetime);
    }

    /// <summary>
    /// Waits for lifetime timeout, then frees this node if still in tree.
    /// </summary>
    private async void StartLifetimeCountdown(float seconds)
    {
        await ToSignal(GetTree().CreateTimer(seconds), "timeout");
        if (IsInsideTree())
            QueueFree();
    }

    /// <summary>
    /// Handles Area2D overlaps by attempting damage on the area's parent Node2D.
    /// </summary>
    private void OnAreaEntered(Area2D area)
    {
        if (area == null) return;

        Node parent = area.GetParent();
        if (parent is Node2D node)
            TryApplyDamage(node);
    }

    /// <summary>
    /// Handles physics body overlaps and attempts damage.
    /// </summary>
    private void OnBodyEntered(Node2D body)
    {
        if (body == null) return;
        TryApplyDamage(body);
    }

    /// Attempts to apply damage to a target character.
    /// Delegates accept/reject logic to CharacterBase via TryRecieveHit.
    /// </summary>
    /// <param name="target">Overlapped Node2D target.</param>
    private void TryApplyDamage(Node2D target)
    {
        if (target is not IDamageable damageable)
            return;

        ulong targetId = target.GetInstanceId();
        if (OneHitPerTarget && _hitTargetIds.Contains(targetId))
            return;

        if (OwnerNode == null)
        {
            GD.PushWarning("Hitbox.Activate() was never called, resulting in no OwnerNode.");
            return;
        }

        bool isNetworked = Multiplayer.MultiplayerPeer != null;

        if (isNetworked && target is CharacterBase targetChar)
        {
            // Reject self-hits (same check singleplayer gets via TryReceiveHit).
            if ((Node)target == OwnerNode) return;

            // Multiplayer — route the hit to whichever peer owns the victim.
            _hitTargetIds.Add(targetId);
            HitLanded?.Invoke();

            long victimAuthority = targetChar.GetMultiplayerAuthority();
            if (victimAuthority == Multiplayer.GetUniqueId())
                // We own the victim — apply damage directly without an RPC.
                targetChar.ReceiveHitRpc(Damage);
            else
                targetChar.RpcId(victimAuthority, nameof(CharacterBase.ReceiveHitRpc), Damage);

            if (DestroyOnFirstHit) QueueFree();
        }
        else
        {
            // Single player — call directly, no RPC needed.
            bool applied = damageable.TryReceiveHit(OwnerNode, this, Damage);
            if (!applied) return;

            _hitTargetIds.Add(targetId);
            HitLanded?.Invoke();
            if (DestroyOnFirstHit) QueueFree();
        }
    }
}
