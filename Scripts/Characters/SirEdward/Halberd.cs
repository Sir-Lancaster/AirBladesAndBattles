using System.Collections.Generic;
using Godot;

public partial class Halberd : Area2D
{
    [Signal]
    public delegate void DespawnedEventHandler(Vector2 position);

    [Export] public float Speed = 550f;
    [Export] public float Lifetime = 0.5f;
    [Export] public int Damage = 8;
    [Export] public bool DestroyOnHit = true;
    // When true, the halberd is a visual-only replica on a remote peer — no damage, no teleport signal.
    public bool VisualOnly = false;

    private readonly HashSet<ulong> _hitTargets = new();
    private Node _ownerNode;
    private Vector2 _direction = Vector2.Right;
    private float _initialRotationDegrees;

    /// <summary>
    /// Initialize the halberd by connecting collision callbacks and starting its lifetime timer.
    /// </summary>
    public override void _Ready()
    {
        BodyEntered += OnBodyEntered;
        AreaEntered += OnAreaEntered;
        StartLifetimeTimer();
        _initialRotationDegrees = RotationDegrees;
    }

    /// <summary>
    /// Set up the halberd launch parameters including owner, direction, and damage.
    /// </summary>
    /// <param name="ownerNode">The node that spawned this halberd.</param>
    /// <param name="direction">The normalized travel direction for the projectile.</param>
    /// <param name="damage">The damage this projectile will apply on hit.</param>
    public void Launch(Node ownerNode, Vector2 direction, int damage)
    {
        _ownerNode = ownerNode;
        _direction = direction.Normalized();
        Damage = damage;

        // Use original rotation, rotate 40° CCW when facing left
        RotationDegrees = _initialRotationDegrees + (_direction.X < 0f ? -40f : 0f);
    }

    /// <summary>
    /// Move the halberd each physics frame according to its direction and speed.
    /// </summary>
    /// <param name="delta">Frame time in seconds.</param>
    public override void _PhysicsProcess(double delta)
    {
        GlobalPosition += _direction * Speed * (float)delta;
    }

    /// <summary>
    /// Start a timer that will despawn the halberd after its lifetime expires.
    /// </summary>
    private async void StartLifetimeTimer()
    {
        if (Lifetime <= 0f) return;

        await ToSignal(GetTree().CreateTimer(Lifetime), "timeout");
        if (IsInsideTree())
            Despawn();
    }

    /// <summary>
    /// Called when another area enters this halberd's area and forwards the event to hit handling.
    /// </summary>
    /// <param name="area">The overlapping Area2D.</param>
    private void OnAreaEntered(Area2D area)
    {
        if (area == null) return;
        if (area.GetParent() is Node2D node)
            TryHit(node);
    }

    /// <summary>
    /// Called when a physics body enters the halberd and forwards the event to hit handling.
    /// </summary>
    /// <param name="body">The body that entered.</param>
    private void OnBodyEntered(Node2D body)
    {
        if (body == null) return;
        TryHit(body);
    }

    /// <summary>
    /// Attempt to apply damage to a target if valid and handle despawn logic after a hit or collision.
    /// </summary>
    /// <param name="target">The node to test for damage application.</param>
    private void TryHit(Node2D target)
    {
        if (VisualOnly || target == null) return;

        if (_ownerNode != null)
        {
            if (target == _ownerNode) return;
            if (_ownerNode.IsAncestorOf(target) || target.IsAncestorOf(_ownerNode)) return;
        }

        if (target is CharacterBase character)
        {
            ulong id = target.GetInstanceId();
            if (_hitTargets.Contains(id)) return;

            _hitTargets.Add(id);
            character.TakeDamage(Damage);

            if (DestroyOnHit)
                Despawn();

            return;
        }

        if (target is StaticBody2D || target is TileMapLayer)
            Despawn();
    }

    /// <summary>
    /// Emit the despawn signal with the current position and free the halberd node.
    /// </summary>
    private void Despawn()
    {
        if (!VisualOnly)
            EmitSignal(SignalName.Despawned, GlobalPosition);
        QueueFree();
    }
}