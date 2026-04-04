using System.Collections.Generic;
using Godot;

/// <summary>
/// Transient damage area used by attacks/specials.
/// Detects overlap targets and delegates hit-validation to <c>CharacterBase</c>.
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

    // Tracks targets already hit by this hitbox when OneHitPerTarget is enabled.
    private readonly HashSet<ulong> _hitTargetIds = new();

    // Prevents starting multiple lifetime timers.
    private bool _lifetimeCountdownStarted;

    /// <summary>
    /// Name of the CharacterBase gate method invoked via reflection:
    /// <c>bool TryRecieveHit(Node attacker, Hitbox hitbox, int damage)</c>.
    /// </summary>
    /// <remarks>
    /// Keep spelling aligned with CharacterBase. If CharacterBase uses "TryReceiveHit",
    /// rename this constant and the method there to match.
    /// </remarks>
    private static readonly StringName TryRecieveHitMethod = "TryRecieveHit";

    /// <summary>
    /// Wires overlap callbacks and starts the lifetime countdown.
    /// </summary>
    public override void _Ready()
    {
        AreaEntered += OnAreaEntered;
        BodyEntered += OnBodyEntered;
        EnsureLifetimeCountdown();
    }

    /// <summary>
    /// Initializes owner, damage, and optional lifetime override.
    /// </summary>
    /// <param name="ownerNode">Attacker node that spawned this hitbox.</param>
    /// <param name="damage">Damage to attempt on valid targets.</param>
    /// <param name="lifetimeSeconds">Optional lifetime override. Negative keeps current value.</param>
    public void Activate(Node ownerNode, int damage, float lifetimeSeconds = -1f)
    {
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

    /// <summary>
    /// Attempts to apply damage to a target character.
    /// Delegates accept/reject logic to CharacterBase via TryRecieveHit.
    /// </summary>
    /// <param name="target">Overlapped Node2D target.</param>
    private void TryApplyDamage(Node2D target)
    {
        if (target is not CharacterBase character)
            return;

        ulong targetId = character.GetInstanceId();
        if (OneHitPerTarget && _hitTargetIds.Contains(targetId))
            return;

        bool applied = false;

        // Preferred flow: CharacterBase owns hit rules and returns whether hit was applied.
        if (character.HasMethod(TryRecieveHitMethod))
        {
            // Zac, this TryRevieveHitMethod is what you need to make to handle the damage check in the base class.
            Variant result = character.Call(TryRecieveHitMethod, OwnerNode, this, Damage);
            applied = result.VariantType == Variant.Type.Bool ? result.AsBool() : true;
        }

        if (!applied) return;

        _hitTargetIds.Add(targetId);

        if (DestroyOnFirstHit) QueueFree();
    }
}
