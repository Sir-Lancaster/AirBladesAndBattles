using Godot;

/// <summary>
/// A stationary training dummy. Takes damage and plays hitstun/death hooks
/// so lasso and attack moves can be tested without a second player.
/// Attach to a CharacterBody2D with a CollisionShape2D.
/// </summary>
public partial class Dummy : CharacterBase
{
    [Export] public string DummyLabel = "Dummy";

    public override void _PhysicsProcess(double delta)
    {
        if (!IsOnFloor())
            Velocity += new Vector2(0, Gravity * (float)delta);

        MoveAndSlide();
    }

    protected override void OnHealthChanged(int oldHp, int newHp)
    {
        GD.Print($"{DummyLabel} HP: {oldHp} -> {newHp}");
    }

    protected override void OnDamaged(int amount)
    {
        GD.Print($"{DummyLabel} took {amount} damage");
    }

    protected override void OnDied()
    {
        GD.Print($"{DummyLabel} died");
    }
}
