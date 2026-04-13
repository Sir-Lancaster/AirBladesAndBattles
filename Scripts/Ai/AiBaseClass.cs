using System;
using System.Collections.Generic;
using Godot;

public abstract partial class AiBaseClass : CharacterBody2D, IDamageable
{
    public enum CharacterState { Idle, Run, Jump, Dodge, Attack, HitStun, Dead }
    public enum AttackDirection { Horizontal, Up, DownAir }
    public enum SpecialDirection { Neutral, Up, horizontal }
    public enum DodgeDirection { Neutral, Horizontal }

    [Export] public int MaxHP = 100;
    [Export] public float MoveSpeed = 200f;
    [Export] public float JumpVelocity = -420f;
    [Export] public float Gravity = 900f;
    [Export] public int BasicDamage = 4;
    [Export] public int MaxJumps = 2;
    public int SpecialDamage => BasicDamage * 2;

    [Export] public float DodgeTime = 0.30f;
    [Export] public float DodgeIFrameTime = 0.28f;
    [Export] public float DodgeCooldown = 0.8f;
    [Export] public float HitIFrameTime = 0.5f;

    [Export] public float AiAttackCooldown = 1.0f;
    [Export] public float AiSpecialCooldown = 5.0f;

    public bool IsDead { get; protected set; }
    public int CurrentHP { get; protected set; }
    public CharacterState CurrentState { get; protected set; }
    public bool IsInvincible { get; private set; }
    private float _hitStunRemaining;
    private float _dodgeRemaining;
    private float _dodgeCooldownRemaining;
    private float _dodgeVelocityX;
    private float _aiAttackCooldownRemaining;
    private float _aiSpecialCooldownRemaining;
    private int _jumpsRemaining;

    /// <summary>Rejects self-hits and hits on dead/invincible characters.</summary>
    public bool TryReceiveHit(Node attacker, Hitbox _hitbox, int damage)
    {
        if (attacker == this) return false;
        if (IsDead || IsInvincible) return false;
        TakeDamage(damage);
        return true;
    }

    public void TakeDamage(int amount)
    {
        if (IsDead || amount <= 0 || IsInvincible) return;

        int oldHp = CurrentHP;
        CurrentHP = Mathf.Max(0, CurrentHP - amount);

        OnHealthChanged(oldHp, CurrentHP);
        OnDamaged(amount);
        GD.Print($"[{Name}] took {amount} damage ({oldHp} -> {CurrentHP} HP)");

        if (CurrentHP == 0)
        {
            IsDead = true;
            SetState(CharacterState.Dead);
            OnDied();
            return;
        }

        Velocity = new Vector2(0f, Velocity.Y);
        _hitStunRemaining = HitIFrameTime;
        IsInvincible = true;
        GetTree().CreateTimer(HitIFrameTime).Timeout += () => IsInvincible = false;
        SetState(CharacterState.HitStun);
    }

    public bool TryStartDodge(DodgeDirection direction)
    {
        if (CurrentState == CharacterState.HitStun ||
            CurrentState == CharacterState.Dead  ||
            CurrentState == CharacterState.Dodge ||
            CurrentState == CharacterState.Attack)
            return false;

        if (_dodgeCooldownRemaining > 0) return false;

        _dodgeRemaining = DodgeTime;
        _dodgeVelocityX = 0f;

        if (direction == DodgeDirection.Horizontal)
        {
            float inputX = AiInput.MoveDirection.X;
            _dodgeVelocityX = (inputX >= 0f ? 1f : -1f) * MoveSpeed * 1.5f;
        }

        IsInvincible = true;
        GetTree().CreateTimer(DodgeIFrameTime).Timeout += () => IsInvincible = false;

        SetState(CharacterState.Dodge);
        OnDodgeStarted(direction, DodgeTime, DodgeIFrameTime);
        return true;
    }

    public void PerformAttack(AttackDirection direction)
    {
        if (CurrentState == CharacterState.HitStun ||
            CurrentState == CharacterState.Dead ||
            CurrentState == CharacterState.Dodge ||
            CurrentState == CharacterState.Attack)
            return;

        // Down-air only makes sense while airborne.
        AttackDirection resolvedDirection = direction;
        if (resolvedDirection == AttackDirection.DownAir && IsOnFloor())
            resolvedDirection = AttackDirection.Horizontal;

        Velocity = new Vector2(0f, Velocity.Y);
        SetState(CharacterState.Attack);
        OnAttackPerformed(resolvedDirection, BasicDamage);
    }

    public void PerformSpecial(SpecialDirection direction)
    {
        if (CurrentState == CharacterState.HitStun ||
            CurrentState == CharacterState.Dead ||
            CurrentState == CharacterState.Dodge ||
            CurrentState == CharacterState.Attack)
            return;

        Velocity = new Vector2(0f, Velocity.Y);
        SetState(CharacterState.Attack);
        OnSpecialPerformed(direction, SpecialDamage);
    }

    protected void SetState(CharacterState newState)
    {
        if (CurrentState == newState) return;
        OnStateChanged(CurrentState, newState);
        CurrentState = newState;
        PlayAnimationForState(newState);
    }

    protected virtual void OnStateChanged(CharacterState currentState, CharacterState newState) { }
    protected virtual void PlayAnimationForState(CharacterState state) { }
    protected virtual void OnHealthChanged(int oldHp, int newHp) { }
    protected virtual void OnDamaged(int amount) { }
    protected virtual void OnDied() { }
    protected virtual void OnAttackPerformed(AttackDirection direction, int damage) { }
    protected virtual void OnSpecialPerformed(SpecialDirection direction, int damage) { }
    protected virtual void OnDodgeStarted(DodgeDirection direction, float dodgeDuration, float iFrameDuration) { }
    protected virtual void OnDodgeEnded(float dodgeCooldown) { }

    // AI input state — set by the subclass each frame before calling base._PhysicsProcess.
    protected struct AiInputState
    {
        public Vector2 MoveDirection;
        public bool AttackJustPressed;
        public bool SpecialJustPressed;
        public bool SpecialHeld;
        public bool DodgeJustPressed;
        public bool JumpJustPressed;
    }
    protected AiInputState AiInput;

    public override void _Ready()
    {
        CurrentHP = MaxHP;
        IsDead = false;
        _jumpsRemaining = MaxJumps;
        CurrentState = CharacterState.Run; // SetState requires a different current state to fire on first call.
        SetState(CharacterState.Idle);

        // Layer 2 = characters; mask 1 = world only.
        // Characters pass through each other and through themselves (multi-player safe).
        CollisionLayer = 2;
        CollisionMask = 1;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (IsDead) return;

        if (!IsOnFloor())
            Velocity += new Vector2(0, Gravity * (float)delta);

        HandleCombatInput();
        HandleMovementInput();

        if (CurrentState == CharacterState.Dodge)
        {
            Velocity = new Vector2(_dodgeVelocityX, Velocity.Y);
            _dodgeRemaining -= (float)delta;
            if (_dodgeRemaining <= 0)
            {
                IsInvincible    = false;
                _dodgeVelocityX = 0f;
                _dodgeCooldownRemaining = DodgeCooldown;
                SetState(CharacterState.Idle);
                OnDodgeEnded(DodgeCooldown);
            }
        }

        if (_dodgeCooldownRemaining > 0)      _dodgeCooldownRemaining -= (float)delta;
        if (_aiAttackCooldownRemaining > 0)   _aiAttackCooldownRemaining -= (float)delta;
        if (_aiSpecialCooldownRemaining > 0)  _aiSpecialCooldownRemaining -= (float)delta;

        if (CurrentState == CharacterState.HitStun && _hitStunRemaining > 0)
        {
            _hitStunRemaining -= (float)delta;
            if (_hitStunRemaining <= 0)
                SetState(CharacterState.Idle);
        }

        MoveAndSlide();
    }

    private void HandleCombatInput()
    {
        if (CurrentState == CharacterState.HitStun || CurrentState == CharacterState.Dead)
            return;

        if (AiInput.AttackJustPressed)
        {
            AttackDirection dir = AttackDirection.Horizontal;
            if (AiInput.MoveDirection.Y < -0.5f) dir = AttackDirection.Up;
            else if (AiInput.MoveDirection.Y > 0.5f && !IsOnFloor()) dir = AttackDirection.DownAir;
            PerformAttack(dir);
        }

        if (AiInput.SpecialJustPressed)
        {
            SpecialDirection dir = AiInput.MoveDirection.Y < -0.5f ? SpecialDirection.Up : SpecialDirection.Neutral;
            PerformSpecial(dir);
        }

        if (AiInput.DodgeJustPressed)
        {
            DodgeDirection dir = Mathf.Abs(AiInput.MoveDirection.X) > 0.3f ? DodgeDirection.Horizontal : DodgeDirection.Neutral;
            TryStartDodge(dir);
        }
    }

    private void HandleMovementInput()
    {
        if (CurrentState == CharacterState.HitStun || CurrentState == CharacterState.Dead)
            return;

        if (CurrentState != CharacterState.Attack && CurrentState != CharacterState.Dodge)
        {
            Velocity = new Vector2(AiInput.MoveDirection.X * MoveSpeed, Velocity.Y);

            if (IsOnFloor())
            {
                _jumpsRemaining = MaxJumps;
                SetState(AiInput.MoveDirection.X == 0 ? CharacterState.Idle : CharacterState.Run);
            }
        }

        if (AiInput.JumpJustPressed && _jumpsRemaining > 0 && CurrentState != CharacterState.Attack)
        {
            _jumpsRemaining--;
            Velocity = new Vector2(Velocity.X, JumpVelocity);
            SetState(CharacterState.Jump);
        }
    }

    // --- Attack selection ---

    protected struct AttackOption
    {
        public float MinAngle, MaxAngle; // degrees, 0-360 (0=right, 90=down, 180=left, 270=up)
        public float MinDist, MaxDist;
        public Action Execute;
        public Func<bool> IsAvailable; // null = always available
        public bool IsSpecial;
    }

    private readonly List<AttackOption> _attackOptions = [];
    private readonly List<AttackOption> _matchingAttacks = [];
    private readonly System.Random _rng = new();

    /// <summary>
    /// Registers an attack option. The AI executes it when the target is within the angle/distance
    /// range and isAvailable (if set) returns true. isSpecial = true applies AiSpecialCooldown after use.
    /// Angles: 0=right, 90=down, 180=left, 270=up. Wrapping ranges supported (e.g. 315–45 = rightward arc).
    /// </summary>
    protected void RegisterAttack(float minAngle, float maxAngle, float minDist, float maxDist,
        Action execute, Func<bool> isAvailable = null, bool isSpecial = false)
    {
        _attackOptions.Add(new AttackOption
        {
            MinAngle = minAngle, MaxAngle = maxAngle,
            MinDist  = minDist,  MaxDist  = maxDist,
            Execute     = execute,
            IsAvailable = isAvailable,
            IsSpecial   = isSpecial
        });
    }

    /// <summary>
    /// Picks and executes a matching attack. Returns false if none match — caller should move instead.
    /// Skips specials on AiSpecialCooldown and any attack whose IsAvailable returns false.
    /// </summary>
    protected bool TrySelectAttack(Vector2 toTarget)
    {
        if (CurrentState == CharacterState.Attack ||
            CurrentState == CharacterState.HitStun ||
            CurrentState == CharacterState.Dead)
            return false;

        if (_aiAttackCooldownRemaining > 0) return false;

        var (angle, dist) = ToAngleDist(toTarget);

        _matchingAttacks.Clear();
        foreach (var opt in _attackOptions)
        {
            if (opt.IsSpecial && _aiSpecialCooldownRemaining > 0) continue;
            if (AngleInRange(angle, opt.MinAngle, opt.MaxAngle) && dist >= opt.MinDist && dist <= opt.MaxDist
                && (opt.IsAvailable == null || opt.IsAvailable()))
                _matchingAttacks.Add(opt);
        }

        if (_matchingAttacks.Count == 0) return false;

        var selected = _matchingAttacks[_rng.Next(_matchingAttacks.Count)];
        selected.Execute();
        _aiAttackCooldownRemaining = AiAttackCooldown;
        if (selected.IsSpecial)
            _aiSpecialCooldownRemaining = AiSpecialCooldown;
        return true;
    }

    /// <summary>
    /// Returns true if the target is within any currently available attack's range.
    /// Use this to suppress movement while waiting for the attack cooldown.
    /// </summary>
    protected bool IsInAttackRange(Vector2 toTarget)
    {
        var (angle, dist) = ToAngleDist(toTarget);
        foreach (var opt in _attackOptions)
        {
            if (opt.IsSpecial && _aiSpecialCooldownRemaining > 0) continue;
            if (AngleInRange(angle, opt.MinAngle, opt.MaxAngle) && dist >= opt.MinDist && dist <= opt.MaxDist
                && (opt.IsAvailable == null || opt.IsAvailable()))
                return true;
        }
        return false;
    }

    protected void MoveTowardTarget(Vector2 toTarget)
    {
        AiInput.MoveDirection = new Vector2(Mathf.Sign(toTarget.X), 0f);
        if (toTarget.Y < -80f)
            AiInput.JumpJustPressed = true;
    }

    private static (float Angle, float Dist) ToAngleDist(Vector2 v)
    {
        float angle = Mathf.RadToDeg(Mathf.Atan2(v.Y, v.X));
        if (angle < 0f) angle += 360f;
        return (angle, v.Length());
    }

    private static bool AngleInRange(float angle, float min, float max)
    {
        if (min <= max) return angle >= min && angle <= max;
        return angle >= min || angle <= max; // wraps around 360
    }
}
