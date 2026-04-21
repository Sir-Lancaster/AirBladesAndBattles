using System;
using System.Collections.Generic;
using Godot;

public abstract partial class AiBaseClass : CharacterBody2D, IDamageable
{
    // ======================================================================
    // Enums
    // ======================================================================

    public enum CharacterState { Idle, Run, Jump, Dodge, Attack, HitStun, Dead }
    public enum AttackDirection { Horizontal, Up, DownAir }
    public enum SpecialDirection { Neutral, Up, horizontal }
    public enum DodgeDirection { Neutral, Horizontal }
    public enum AiDifficulty { Easy, Medium, Hard }

    /// <summary>Normal: standard chase-and-attack. Aggressive: avoid repeating the last attack; fallback to up-attack.</summary>
    public enum BehaviorMode { Normal, Aggressive }

    // ======================================================================
    // Exports — core stats
    // ======================================================================

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
    [Export] public float HitIFrameTime = 0.25f;

    [Export] public float AiAttackCooldown = 1.0f;
    [Export] public float AiSpecialCooldown = 5.0f;
    [Export] public AiDifficulty Difficulty = AiDifficulty.Hard;

    /// <summary>Attacks fired before randomly switching BehaviorMode.</summary>
    [Export] public int HitsPerModeChange = 3;

    /// <summary>Seconds the AI backs away after landing an attack.</summary>
    [Export] public float RetreatDuration = 0.5f;

    [Export] public float HesitationMinInterval = 3f;
    [Export] public float HesitationMaxInterval = 8f;
    /// <summary>Base hesitation pause duration (randomized ±25%).</summary>
    [Export] public float HesitationDuration = 0.2f;

    /// <summary>Pixels the target must be above the AI before it jumps to a higher platform.</summary>
    [Export] public float PlatformJumpThreshold = 100f;
    /// <summary>Minimum seconds between deliberate jumps.</summary>
    [Export] public float JumpCooldown = 0.8f;

    /// <summary>Pixels ahead the edge-detection ray is cast.</summary>
    [Export] public float EdgeRayLookAhead = 36f;
    /// <summary>Pixels down the edge ray looks for ground.</summary>
    [Export] public float EdgeRayDepth = 500f;

    /// <summary>Radius of the danger-zone Area2D that triggers reactive dodge/jump.</summary>
    [Export] public float ThreatDetectionRadius = 150f;
    /// <summary>Preferred spacing from the target in pixels.</summary>
    [Export] public float PreferredCombatRange = 100f;
    /// <summary>During airborne recovery, prefer the target's platform if it's within this horizontal distance.</summary>
    [Export] public float RecoveryPreferTargetRange = 400f;

    // ======================================================================
    // Target tracking
    // ======================================================================

    protected Node2D _target;
    private bool IsTargetDead => _target == null || IsNodeDead(_target);

    private static bool IsNodeDead(Node2D node)
    {
        if (node is CharacterBase cb) return cb.IsDead;
        if (node is AiBaseClass ai)   return ai.IsDead;
        return true;
    }

    private void UpdateTarget()
    {
        Node2D nearest = null;
        float nearestDist = float.MaxValue;
        foreach (Node node in GetTree().GetNodesInGroup("characters"))
        {
            if (node == this) continue;
            if (node is not Node2D n) continue;
            if (IsNodeDead(n)) continue;
            float dist = GlobalPosition.DistanceTo(n.GlobalPosition);
            if (dist < nearestDist) { nearestDist = dist; nearest = n; }
        }
        _target = nearest;
    }

    // ======================================================================
    // Public state
    // ======================================================================

    public bool IsDead { get; protected set; }
    public int CurrentHP { get; protected set; }
    public CharacterState CurrentState { get; protected set; }
    public bool IsInvincible { get; private set; }
    public BehaviorMode CurrentBehaviorMode { get; private set; }

    protected bool IsRetreating => _retreatTimer > 0f;
    protected bool IsHesitating => _hesitationActiveFor > 0f;

    // ======================================================================
    // Private fields
    // ======================================================================

    private float _hitStunRemaining;
    private float _dodgeRemaining;
    private float _dodgeCooldownRemaining;
    private float _dodgeVelocityX;
    private float _aiAttackCooldownRemaining;
    private float _aiSpecialCooldownRemaining;
    private int   _jumpsRemaining;

    private int   _hitsSinceLastModeChange;
    private AttackOption? _lastUsedAttack;
    private float _retreatTimer;
    private float _nextHesitationIn;
    private float _hesitationActiveFor;
    private float _jumpCooldownRemaining;
    private float _jumpGracePeriod;

    private Area2D    _dangerZone;
    private RayCast2D _edgeRay;

    private readonly System.Random _rng = new();

    // ======================================================================
    // IDamageable
    // ======================================================================

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

    // ======================================================================
    // Combat actions
    // ======================================================================

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

    // ======================================================================
    // State machine
    // ======================================================================

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

    // ======================================================================
    // AI input state — set by subclass each frame before base._PhysicsProcess
    // ======================================================================

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

    // ======================================================================
    // Godot lifecycle
    // ======================================================================

    public override void _Ready()
    {
        AiAttackCooldown = Difficulty switch
        {
            AiDifficulty.Easy   => 3.0f,
            AiDifficulty.Medium => 2.0f,
            _                   => 1.0f,
        };

        AddToGroup("characters");
        CurrentHP = MaxHP;
        IsDead = false;
        _jumpsRemaining = MaxJumps;
        CurrentState = CharacterState.Run; // SetState requires a different current state to fire on first call.
        SetState(CharacterState.Idle);

        CollisionLayer = 2;
        CollisionMask = 1;

        CurrentBehaviorMode = BehaviorMode.Normal;

        _nextHesitationIn = HesitationMinInterval
            + (float)_rng.NextDouble() * (HesitationMaxInterval - HesitationMinInterval);

        SetupDangerZone();
        SetupEdgeDetection();
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

        if (_dodgeCooldownRemaining > 0)     _dodgeCooldownRemaining -= (float)delta;
        if (_aiAttackCooldownRemaining > 0)  _aiAttackCooldownRemaining -= (float)delta;
        if (_aiSpecialCooldownRemaining > 0) _aiSpecialCooldownRemaining -= (float)delta;
        if (_jumpCooldownRemaining > 0)      _jumpCooldownRemaining -= (float)delta;
        if (_retreatTimer > 0)               _retreatTimer -= (float)delta;
        if (_jumpGracePeriod > 0)            _jumpGracePeriod -= (float)delta;

        if (CurrentState == CharacterState.HitStun && _hitStunRemaining > 0)
        {
            _hitStunRemaining -= (float)delta;
            if (_hitStunRemaining <= 0)
                SetState(CharacterState.Idle);
        }

        TickHesitation((float)delta);
        MoveAndSlide();
    }

    // ======================================================================
    // Input handlers
    // ======================================================================

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

        if (AiInput.JumpJustPressed && _jumpsRemaining > 0 && CurrentState != CharacterState.Attack && CurrentState != CharacterState.Dodge)
        {
            _jumpsRemaining--;
            Velocity = new Vector2(Velocity.X, JumpVelocity);
            SetState(CharacterState.Jump);
        }
    }

    // ======================================================================
    // Main AI behavior — call from subclass _PhysicsProcess
    // ======================================================================

    protected void RunAiBehavior()
    {
        UpdateTarget();
        if (_target == null) return;
        if (CurrentState == CharacterState.Dodge) return;
        if (HandleAirborneRecovery()) return;
        if (IsHesitating) return;

        switch (CurrentBehaviorMode)
        {
            case BehaviorMode.Normal:     RunNormalBehavior();     break;
            case BehaviorMode.Aggressive: RunAggressiveBehavior(); break;
        }
    }

    private void RunNormalBehavior()
    {
        Vector2 toTarget = _target.GlobalPosition - GlobalPosition;

        if (IsRetreating)
        {
            MoveAwayFromTarget(toTarget);
            return;
        }

        if (TrySelectAttack(toTarget))
            _retreatTimer = RetreatDuration;
        else
            MoveTowardTarget(toTarget);
    }

    private void RunAggressiveBehavior()
    {
        Vector2 toTarget = _target.GlobalPosition - GlobalPosition;
        if (!TrySelectAttackAggressive(toTarget) && _aiAttackCooldownRemaining <= 0)
        {
            AggressiveFallbackAttack();
            _aiAttackCooldownRemaining = AiAttackCooldown;
        }
    }

    /// <summary>
    /// Picks any in-range attack except the last one used.
    /// Returns false if no alternative is available.
    /// </summary>
    private bool TrySelectAttackAggressive(Vector2 toTarget)
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
            if (_lastUsedAttack.HasValue && opt.Execute == _lastUsedAttack.Value.Execute) continue;
            if (AngleInRange(angle, opt.MinAngle, opt.MaxAngle) && dist >= opt.MinDist && dist <= opt.MaxDist
                && (opt.IsAvailable == null || opt.IsAvailable()))
                _matchingAttacks.Add(opt);
        }

        if (_matchingAttacks.Count == 0) return false;

        OnAttackExecuted(_matchingAttacks[_rng.Next(_matchingAttacks.Count)]);
        return true;
    }

    /// <summary>Override to provide the fallback used in Aggressive mode when no alternative attack is in range.</summary>
    protected virtual void AggressiveFallbackAttack() { }

    // ======================================================================
    // Movement helpers
    // ======================================================================

    /// <summary>
    /// Moves toward toTarget with platform/edge awareness.
    /// Drops off edges when target is below, jumps gaps when target is level/above.
    /// Jumps when target is on a significantly higher platform.
    /// </summary>
    protected void MoveTowardTarget(Vector2 toTarget)
    {
        if (toTarget.Length() <= PreferredCombatRange)
        {
            MoveAwayFromTarget(toTarget);
            return;
        }

        float dirX = Mathf.Sign(toTarget.X);

        if (IsOnFloor() && IsEdgeAhead(dirX))
        {
            if (toTarget.Y > 60f)
            {
                // Target is below — drop off the edge.
                AiInput.JumpJustPressed = true;
                AiInput.MoveDirection = new Vector2(dirX, 0f);
            }
            else if (_jumpCooldownRemaining <= 0f)
            {
                // Gap ahead — jump forward to bridge it.
                AiInput.MoveDirection = new Vector2(dirX, 0f);
                AiInput.JumpJustPressed = true;
                _jumpCooldownRemaining = 0.05f;
                _jumpGracePeriod = Mathf.Abs(JumpVelocity) / Gravity;
            }
            else
            {
                AiInput.MoveDirection = Vector2.Zero;
            }
            return;
        }

        AiInput.MoveDirection = new Vector2(dirX, 0f);

        if (toTarget.Y < -PlatformJumpThreshold && IsOnFloor() && _jumpCooldownRemaining <= 0f)
        {
            AiInput.JumpJustPressed = true;
            _jumpCooldownRemaining = JumpCooldown;
            _jumpGracePeriod = 2f * Mathf.Abs(JumpVelocity) / Gravity;
        }
    }

    protected void MoveAwayFromTarget(Vector2 toTarget)
    {
        float dirX = -Mathf.Sign(toTarget.X);

        if (IsOnFloor() && IsEdgeAhead(dirX))
        {
            // Edge blocks retreat — cross to the other side of the target instead.
            AiInput.MoveDirection = new Vector2(Mathf.Sign(toTarget.X), 0f);
            return;
        }

        AiInput.MoveDirection = new Vector2(dirX, 0f);
    }

    // ======================================================================
    // Platform / edge awareness
    // ======================================================================

    private void SetupEdgeDetection()
    {
        _edgeRay = new RayCast2D
        {
            Name           = "EdgeRay",
            Enabled        = true,
            CollisionMask  = 1,
            TargetPosition = new Vector2(0f, EdgeRayDepth)
        };
        AddChild(_edgeRay);
    }

    /// <summary>Returns true if no floor exists within EdgeRayDepth pixels ahead in directionX.</summary>
    private bool IsEdgeAhead(float directionX)
    {
        if (directionX == 0f || _edgeRay == null) return false;
        _edgeRay.Position = new Vector2(directionX * EdgeRayLookAhead, 0f);
        _edgeRay.ForceRaycastUpdate();
        return !_edgeRay.IsColliding();
    }

    /// <summary>
    /// Handles off-stage recovery. Returns true if a recovery action was taken.
    /// Priority: double-jump toward nearest platform → character-specific recovery move.
    /// </summary>
    private bool HandleAirborneRecovery()
    {
        if (IsAbovePlatform() || _jumpGracePeriod > 0f) return false;

        AiInput.MoveDirection = new Vector2(FindNearestPlatformDirection(), 0f);
        if (_jumpsRemaining > 0)
        {
            if (_jumpCooldownRemaining <= 0f)
            {
                AiInput.JumpJustPressed = true;
                _jumpCooldownRemaining = 0.05f;
            }
            return true;
        }
        TryRecoveryMove();
        return true;
    }

    private bool IsAbovePlatform()
    {
        _edgeRay.Position = Vector2.Zero;
        _edgeRay.ForceRaycastUpdate();
        return _edgeRay.IsColliding();
    }

    /// <summary>
    /// Returns the X direction toward the best recovery platform.
    /// Prefers the target's platform if it's within RecoveryPreferTargetRange; otherwise uses the nearest.
    /// </summary>
    private float FindNearestPlatformDirection()
    {
        float preferredDir = !IsTargetDead
            ? Mathf.Sign(_target.GlobalPosition.X - GlobalPosition.X)
            : 1f;

        float targetSideDist   = float.MaxValue;
        float oppositeSideDist = float.MaxValue;

        for (float offset = 50f; offset <= 600f; offset += 50f)
        {
            if (targetSideDist == float.MaxValue)
            {
                _edgeRay.Position = new Vector2(preferredDir * offset, 0f);
                _edgeRay.ForceRaycastUpdate();
                if (_edgeRay.IsColliding()) targetSideDist = offset;
            }

            if (oppositeSideDist == float.MaxValue)
            {
                _edgeRay.Position = new Vector2(-preferredDir * offset, 0f);
                _edgeRay.ForceRaycastUpdate();
                if (_edgeRay.IsColliding()) oppositeSideDist = offset;
            }

            if (targetSideDist != float.MaxValue && oppositeSideDist != float.MaxValue)
                break;
        }

        if (targetSideDist <= RecoveryPreferTargetRange) return preferredDir;
        if (oppositeSideDist < targetSideDist) return -preferredDir;
        return preferredDir;
    }

    /// <summary>Override to provide a character-specific aerial recovery move. Return true if initiated.</summary>
    protected virtual bool TryRecoveryMove() => false;

    // ======================================================================
    // Threat detection
    // ======================================================================

    private void SetupDangerZone()
    {
        _dangerZone = new Area2D { Name = "DangerZone", CollisionLayer = 0, CollisionMask = 0b1111 };
        _dangerZone.AddChild(new CollisionShape2D
        {
            Shape = new CircleShape2D { Radius = ThreatDetectionRadius }
        });
        AddChild(_dangerZone);
        _dangerZone.AreaEntered += OnDangerZoneAreaEntered;
    }

    private void OnDangerZoneAreaEntered(Area2D area)
    {
        if (area is not Hitbox hitbox) return;
        if (hitbox.OwnerNode == this || hitbox.OwnerNode == null) return;
        bool isProjectile = hitbox.GetParent() is not IDamageable;
        OnThreatDetected(hitbox, isProjectile);
    }

    /// <summary>
    /// Called when an enemy hitbox/projectile enters the danger zone.
    /// Default: attack incoming projectiles in range; dodge or jump away from melee.
    /// </summary>
    protected virtual void OnThreatDetected(Hitbox threat, bool isProjectile)
    {
        if (IsInvincible || CurrentState == CharacterState.Dead) return;
        if (!IsAbovePlatform() && _jumpGracePeriod <= 0f) return;

        Vector2 toThreat = threat.GlobalPosition - GlobalPosition;

        if (isProjectile && IsInAttackRange(toThreat))
            AiInput.AttackJustPressed = true;
        else
            TryDodgeOrJump(toThreat);
    }

    private void TryDodgeOrJump(Vector2 toThreat)
    {
        AiInput.MoveDirection = new Vector2(Mathf.Sign(toThreat.X), 0f);

        DodgeDirection dodgeDir = Mathf.Abs(toThreat.X) > 30f
            ? DodgeDirection.Horizontal
            : DodgeDirection.Neutral;

        bool dodged = TryStartDodge(dodgeDir);

        if (!dodged && _jumpCooldownRemaining <= 0f)
        {
            AiInput.JumpJustPressed = true;
            _jumpCooldownRemaining = JumpCooldown;
        }
    }

    // ======================================================================
    // Behavior mode
    // ======================================================================

    private void OnAttackExecuted(AttackOption selected)
    {
        selected.Execute();
        _lastUsedAttack = selected;
        _aiAttackCooldownRemaining = AiAttackCooldown;
        if (selected.IsSpecial)
            _aiSpecialCooldownRemaining = AiSpecialCooldown;

        if (++_hitsSinceLastModeChange >= HitsPerModeChange)
        {
            _hitsSinceLastModeChange = 0;
            CurrentBehaviorMode = (BehaviorMode)_rng.Next(2);
        }
    }

    // ======================================================================
    // Hesitation
    // ======================================================================

    private void TickHesitation(float delta)
    {
        if (_hesitationActiveFor > 0f)
        {
            _hesitationActiveFor -= delta;
            return;
        }

        _nextHesitationIn -= delta;
        if (_nextHesitationIn <= 0f)
        {
            _hesitationActiveFor = HesitationDuration * (float)(_rng.NextDouble() * 0.5 + 0.75);
            _nextHesitationIn = HesitationMinInterval
                + (float)_rng.NextDouble() * (HesitationMaxInterval - HesitationMinInterval);
        }
    }

    // ======================================================================
    // Attack selection
    // ======================================================================

    protected struct AttackOption
    {
        public float MinAngle, MaxAngle; // degrees: 0=right, 90=down, 180=left, 270=up
        public float MinDist, MaxDist;
        public Action Execute;
        public Func<bool> IsAvailable; // null = always available
        public bool IsSpecial;
    }

    private readonly List<AttackOption> _attackOptions = [];
    private readonly List<AttackOption> _matchingAttacks = [];

    /// <summary>
    /// Registers an attack. The AI fires it when the target falls within the angle/distance window.
    /// Angles: 0=right, 90=down, 180=left, 270=up. Wrapping ranges supported (e.g. 315–45).
    /// </summary>
    protected void RegisterAttack(float minAngle, float maxAngle, float minDist, float maxDist,
        Action execute, Func<bool> isAvailable = null, bool isSpecial = false)
    {
        _attackOptions.Add(new AttackOption
        {
            MinAngle    = minAngle,    MaxAngle    = maxAngle,
            MinDist     = minDist,     MaxDist     = maxDist,
            Execute     = execute,
            IsAvailable = isAvailable,
            IsSpecial   = isSpecial
        });
    }

    /// <summary>Picks and executes a matching attack. Returns false if none match or cooldown is active.</summary>
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

        OnAttackExecuted(_matchingAttacks[_rng.Next(_matchingAttacks.Count)]);
        return true;
    }

    /// <summary>Returns true if any registered attack can reach toTarget right now.</summary>
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

    // ======================================================================
    // Utilities
    // ======================================================================

    private static (float Angle, float Dist) ToAngleDist(Vector2 v)
    {
        float angle = Mathf.RadToDeg(Mathf.Atan2(v.Y, v.X));
        if (angle < 0f) angle += 360f;
        return (angle, v.Length());
    }

    private static bool AngleInRange(float angle, float min, float max)
    {
        if (min <= max) return angle >= min && angle <= max;
        return angle >= min || angle <= max;
    }
}
