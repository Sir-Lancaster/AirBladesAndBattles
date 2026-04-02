using System.Collections.Generic;
using Godot;

public partial class Hitbox : Area2D
{
    [Export] public int Damage = 4;
    [Export] public float Lifetime = 0.12f;
    [Export] public bool OneHitPerTarget = true;
    [Export] public bool DestroyOnFirstHit = false;

    public Node OwnerNode { get; private set; }

    private readonly HashSet<ulong> _hitTargetIds = new();
    private bool _lifetimeCountdownStarted;

    public override void _Ready()
    {
        AreaEntered += OnAreaEntered;
        BodyEntered += OnBodyEntered;
        //EnsureLifetimeCountdown(Lifetime);
    }

    public void Activate(Node ownerNode, int damage, float lifetimeSeconds = -1f)
    {
        OwnerNode = ownerNode;
        Damage = damage;

        if (lifetimeSeconds < 0f)
            Lifetime = 0f; // Disable countdown
        else
            Lifetime = lifetimeSeconds;

        EnsureLifetimeCountdown(Lifetime);
    }

    private void EnsureLifetimeCountdown(float lifetime)
    {
        if (_lifetimeCountdownStarted || lifetime <= 0f)
            return;

        _lifetimeCountdownStarted = true;
        StartLifetimeCountdown(lifetime);
    }

    private async void StartLifetimeCountdown(float seconds)
    {
        await ToSignal(GetTree().CreateTimer(seconds), "timeout");
        if (IsInsideTree())
            QueueFree();
    }

    private void OnAreaEntered(Area2D area)
    {
        if (area == null) return;

        Node parent = area.GetParent();
        if (parent is Node2D node)
            TryApplyDamage(node);
    }

    private void OnBodyEntered(Node2D body)
    {
        if (body == null) return;
        TryApplyDamage(body);
    }

    private void TryApplyDamage(Node2D target)
    {
        if (target == null)
            return;

        if (OwnerNode != null)
        {
            if (target == OwnerNode)
                return;

            if (OwnerNode.IsAncestorOf(target) || target.IsAncestorOf(OwnerNode))
                return;
        }

        ulong targetId = target.GetInstanceId();
        if (OneHitPerTarget && _hitTargetIds.Contains(targetId))
            return;

        if (target is CharacterBase character)
        {
            character.TakeDamage(Damage);
            _hitTargetIds.Add(targetId);

            if (DestroyOnFirstHit)
                QueueFree();
        }
    }
}

