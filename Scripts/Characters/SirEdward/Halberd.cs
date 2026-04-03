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

    private readonly HashSet<ulong> _hitTargets = new();
    private Node _ownerNode;
    private Vector2 _direction = Vector2.Right;

    public override void _Ready()
    {
        BodyEntered += OnBodyEntered;
        AreaEntered += OnAreaEntered;
        StartLifetimeTimer();
    }

    public void Launch(Node ownerNode, Vector2 direction, int damage)
    {
        _ownerNode = ownerNode;
        _direction = direction.Normalized();
        Damage = damage;
    }

    public override void _PhysicsProcess(double delta)
    {
        GlobalPosition += _direction * Speed * (float)delta;
    }

    private async void StartLifetimeTimer()
    {
        if (Lifetime <= 0f) return;

        await ToSignal(GetTree().CreateTimer(Lifetime), "timeout");
        if (IsInsideTree())
            Despawn();
    }

    private void OnAreaEntered(Area2D area)
    {
        if (area == null) return;
        if (area.GetParent() is Node2D node)
            TryHit(node);
    }

    private void OnBodyEntered(Node2D body)
    {
        if (body == null) return;
        TryHit(body);
    }

    private void TryHit(Node2D target)
    {
        if (target == null) return;

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

    private void Despawn()
    {
        EmitSignal(SignalName.Despawned, GlobalPosition);
        QueueFree();
    }
}