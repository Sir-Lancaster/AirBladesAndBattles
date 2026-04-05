using Godot;

/// <summary>
/// Horizontal projectile for the Steampunk's neutral special.
/// Extends Hitbox so all damage/one-hit logic is inherited.
/// Travels in the direction the Steampunk is facing until it hits
/// a character or a physics body (wall), then frees itself.
/// Must be added to the scene tree BEFORE Launch() is called.
/// </summary>
public partial class SteampunkProjectile : Hitbox
{
    [Export] public float Speed = 800f;
    [Export] public float MaxRange = 800f;

    private Vector2 _direction;
    private Vector2 _origin;
    private bool _launched;

    public override void _Ready()
    {
        base._Ready();
        DestroyOnFirstHit = true;
        BodyEntered += OnWallHit;
    }

    /// <summary>
    /// Launches horizontally based on facing direction. Call this after AddChild().
    /// </summary>
    public void Launch(Node ownerNode, int damage, bool facingLeft)
    {
        LaunchInDirection(ownerNode, damage, new Vector2(facingLeft ? -1f : 1f, 0f));
    }

    /// <summary>
    /// Launches downward. Call this after AddChild().
    /// </summary>
    public void LaunchDown(Node ownerNode, int damage)
    {
        LaunchInDirection(ownerNode, damage, Vector2.Down);
    }

    private void LaunchInDirection(Node ownerNode, int damage, Vector2 direction)
    {
        _direction = direction.Normalized();
        _origin = GlobalPosition;
        _launched = true;
        Activate(ownerNode, damage, -1f);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_launched) return;

        GlobalPosition += _direction * Speed * (float)delta;

        if (GlobalPosition.DistanceTo(_origin) >= MaxRange)
            QueueFree();
    }

    // Destroy on contact with any non-character physics body (walls, platforms, etc.)
    private void OnWallHit(Node2D body)
    {
        if (body is CharacterBase) return;
        QueueFree();
    }
}
