using System.Collections.Generic;
using Godot;

public partial class Hitbox : Area2D
{
    [Export] public int Damage = 4;
    [Export] public bool OneHitPerTarget = true;
    [Export] public bool DestroyOnFirstHit = false;

    public Node OwnerNode { get; private set; }

    private readonly HashSet<ulong> _hitTargetIds = new();

    public override void _Ready()
    {
        AreaEntered += OnAreaEntered;
        BodyEntered += OnBodyEntered;
    }

    public void Activate(Node ownerNode, int damage, float lifetimeSeconds = -1f)
    {
        OwnerNode = ownerNode;
        Damage = damage;

        if (lifetimeSeconds > 0f)
            StartLifetimeCountdown(lifetimeSeconds);
    }

    public void UpdateDamage(int damage) => Damage = damage;

    private async void StartLifetimeCountdown(float seconds)
    {
        await ToSignal(GetTree().CreateTimer(seconds), "timeout");
        if (IsInsideTree())
            QueueFree();
    }

    private void OnAreaEntered(Area2D area)
    {
        if (area?.GetParent() is Node2D parent)
            TryApplyDamage(parent);
    }

    private void OnBodyEntered(Node2D body) => TryApplyDamage(body);

    private void TryApplyDamage(Node2D target)
    {
        if (target == null || target == OwnerNode) return;

        if (OwnerNode != null && (OwnerNode.IsAncestorOf(target) || target.IsAncestorOf(OwnerNode)))
            return;

        ulong targetId = target.GetInstanceId();
        if (OneHitPerTarget && _hitTargetIds.Contains(targetId)) return;

        if (target is CharacterBase character)
        {
            character.TakeDamage(Damage);
            _hitTargetIds.Add(targetId);

            if (DestroyOnFirstHit)
                QueueFree();
        }
    }
}