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

    /// <summary>
    /// Controls attack frequency, retreat duration, and hesitation rate.
    /// Switched automatically based on HP and random timed rolls.
    /// </summary>
    public enum AggressivenessMode { Cautious, Neutral, Aggressive, Berserk }

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

    // ======================================================================
    // Exports — target
    // ======================================================================

    /// <summary>The character this AI will chase and attack.</summary>
    [Export] public CharacterBase Target;

    // ======================================================================
    // Exports — aggressiveness
    // ======================================================================

    /// <summary>Aggressiveness mode at spawn.</summary>
    [Export] public AggressivenessMode StartingMode = AggressivenessMode.Neutral;

    /// <summary>Seconds between random mode re-rolls.</summary>
    [Export] public float AggroModeChangeInterval = 8f;

    /// <summary>HP fraction below which the AI locks into Berserk regardless of roll.</summary>
    [Export] public float BerserkHpThreshold = 0.25f;

    // ======================================================================
    // Exports — retreat
    // ======================================================================

    /// <summary>
    /// Base duration (seconds) the AI backs away after landing an attack.
    /// Scaled by aggressiveness: Berserk barely retreats, Cautious retreats longest.
    /// </summary>
    [Export] public float RetreatDuration = 0.5f;

    // ======================================================================
    // Exports — hesitation
    // ======================================================================

    /// <summary>Minimum seconds between random hesitation pauses.</summary>
    [Export] public float HesitationMinInterval = 3f;

    /// <summary>Maximum seconds between random hesitation pauses.</summary>
    [Export] public float HesitationMaxInterval = 8f;

    /// <summary>Base duration of a hesitation pause in seconds (randomized ±25%).</summary>
    [Export] public float HesitationDuration = 0.2f;

    // ======================================================================
    // Exports — jump control
    // ======================================================================

    /// <summary>
    /// Target must be at least this many pixels above the AI before it
    /// considers jumping to a higher platform. Prevents constant bunny-hopping.
    /// </summary>
    [Export] public float PlatformJumpThreshold = 100f;

    /// <summary>Minimum seconds between deliberate jump attempts.</summary>
    [Export] public float JumpCooldown = 0.8f;

    // ======================================================================
    // Exports — platform / edge awareness
    // ======================================================================

    /// <summary>
    /// How far ahead (pixels, in the movement direction) the edge-detection ray
    /// is cast. Increase if the AI still walks off edges at high speed.
    /// </summary>
    [Export] public float EdgeRayLookAhead = 36f;

    /// <summary>
    /// How far down the edge ray looks for ground. Should be at least a full
    /// character height so a short step-down isn't mistaken for a cliff.
    /// </summary>
    [Export] public float EdgeRayDepth = 80f;

    /// <summary>
    /// Seconds the AI must have been continuously airborne before it considers
    /// itself "in trouble" and triggers a recovery move. A normal full jump arc
    /// should land well before this threshold.
    /// </summary>
    [Export] public float RecoveryAirborneThreshold = 0.9f;

    // ======================================================================
    // Exports — threat detection
    // ======================================================================

    /// <summary>
    /// Radius of the danger-zone Area2D that triggers reactive dodge/jump.
    /// Tune this to match the reach of enemy attacks.
    /// </summary>
    [Export] public float ThreatDetectionRadius = 150f;

    // ======================================================================
    // Public state
    // ======================================================================

    public bool IsDead { get; protected set; }
    public int CurrentHP { get; protected set; }
    public CharacterState CurrentState { get; protected set; }
    public bool IsInvincible { get; private set; }

    /// <summary>Current aggressiveness mode — updated automatically each tick.</summary>
    public AggressivenessMode CurrentMode { get; private set; }

    /// <summary>True while the AI is in the post-attack backing-away phase.</summary>
    protected bool IsRetreating => _retreatTimer > 0f;

    /// <summary>True during a random hesitation pause (AI does nothing).</summary>
    protected bool IsHesitating => _hesitationActiveFor > 0f;

    // ======================================================================
    // Private — combat timers
    // ======================================================================

    private float _hitStunRemaining;
    private float _dodgeRemaining;
    private float _dodgeCooldownRemaining;
    private float _dodgeVelocityX;
    private float _aiAttackCooldownRemaining;
    private float _aiSpecialCooldownRemaining;
    private int _jumpsRemaining;

    // ======================================================================
    // Private — AI-brain timers & state
    // ======================================================================

    private float _aggroModeTimer;       // counts down to the next aggressiveness re-roll
    private float _retreatTimer;         // counts down the post-attack retreat phase
    private float _nextHesitationIn;     // counts down to the next hesitation pause
    private float _hesitationActiveFor;  // counts down the current hesitation pause
    private float _jumpCooldownRemaining; // prevents spamming jumps
    private float _airborneTime;          // how long we've been off the floor this air-state

    private Area2D _dangerZone;   // child Area2D that detects incoming hitboxes
    private RayCast2D _edgeRay;   // downward ray cast ahead in the movement direction

    private readonly System.Random _rng = new();

    // ======================================================================
    // IDamageable
    // ======================================================================

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

    /// <summary>
    /// Called when this character takes damage. Base implementation spikes aggressiveness.
    /// If you override this, call base.OnDamaged(amount) to keep the aggro spike.
    /// </summary>
    protected virtual void OnDamaged(int amount)
    {
        // Getting hit makes the AI angrier — unless it's already in a frenzy.
        if (CurrentMode != AggressivenessMode.Berserk)
            CurrentMode = AggressivenessMode.Aggressive;

        // Reset the mode timer so the aggro spike lasts before the next re-roll.
        _aggroModeTimer = AggroModeChangeInterval;
    }

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
        CurrentHP = MaxHP;
        IsDead = false;
        _jumpsRemaining = MaxJumps;
        CurrentState = CharacterState.Run; // SetState requires a different current state to fire on first call.
        SetState(CharacterState.Idle);

        // Layer 2 = characters; mask 1 = world only.
        CollisionLayer = 2;
        CollisionMask = 1;

        // Aggressiveness — start in the configured mode and schedule the first re-roll.
        CurrentMode = StartingMode;
        _aggroModeTimer = AggroModeChangeInterval;

        // Hesitation — stagger the first pause so all AIs don't freeze at the same moment.
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

        // Track how long we've been continuously airborne.
        if (IsOnFloor()) _airborneTime = 0f;
        else             _airborneTime += (float)delta;

        if (CurrentState == CharacterState.HitStun && _hitStunRemaining > 0)
        {
            _hitStunRemaining -= (float)delta;
            if (_hitStunRemaining <= 0)
                SetState(CharacterState.Idle);
        }

        TickAggressiveness((float)delta);
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

        if (AiInput.JumpJustPressed && _jumpsRemaining > 0 && CurrentState != CharacterState.Attack)
        {
            _jumpsRemaining--;
            Velocity = new Vector2(Velocity.X, JumpVelocity);
            SetState(CharacterState.Jump);
        }
    }

    // ======================================================================
    // Main AI behavior — call this from subclass _PhysicsProcess
    // ======================================================================

    /// <summary>
    /// Unified AI decision loop. Call this each frame from the subclass _PhysicsProcess
    /// in place of manually calling TrySelectAttack / MoveTowardTarget.
    ///
    /// Decision priority each frame:
    ///   1. Recovery     → airborne too long, use double-jump or recovery move
    ///   2. Hesitating   → do nothing (random idle pause)
    ///   3. Retreating   → back away (post-attack cooldown phase)
    ///   4. Attack fires → start retreat timer
    ///   5. Not in range → chase target
    ///   6. In range but on cooldown → stand still (confident waiting)
    /// </summary>
    protected void RunAiBehavior()
    {
        if (Target == null || Target.IsDead) return;

        // Recovery takes highest priority — if we've been airborne too long
        // something went wrong and we need to try to get back to the stage.
        if (HandleAirborneRecovery()) return;

        // Random idle pause — simulates reaction delay and makes rhythm unpredictable.
        if (IsHesitating) return;

        Vector2 toTarget = Target.GlobalPosition - GlobalPosition;

        // Post-attack retreat — back off to create attack breathing room.
        if (IsRetreating)
        {
            MoveAwayFromTarget(toTarget);
            return;
        }

        if (TrySelectAttack(toTarget))
        {
            // Attack fired — start the retreat phase scaled by aggressiveness.
            _retreatTimer = RetreatDuration * GetRetreatMultiplier();
        }
        else if (!IsInAttackRange(toTarget))
        {
            // Nothing can fire and we're out of range — close the gap.
            MoveTowardTarget(toTarget);
        }
        // If in range but on cooldown: stand still. This reads as deliberate
        // patience rather than confused inaction.
    }

    // ======================================================================
    // Movement helpers
    // ======================================================================

    /// <summary>
    /// Moves toward the target. Stops at edges rather than walking off.
    /// Jumps only when the target is on a meaningfully higher platform.
    /// </summary>
    protected void MoveTowardTarget(Vector2 toTarget)
    {
        float dirX = Mathf.Sign(toTarget.X);

        // Don't walk off the edge — stop and optionally jump instead.
        if (IsOnFloor() && IsEdgeAhead(dirX))
        {
            AiInput.MoveDirection = Vector2.Zero;
            // If the target is above (they're on the platform we just reached the edge of),
            // jump up to reach them.
            if (toTarget.Y < -PlatformJumpThreshold && _jumpCooldownRemaining <= 0f)
            {
                AiInput.JumpJustPressed = true;
                _jumpCooldownRemaining = JumpCooldown;
            }
            return;
        }

        AiInput.MoveDirection = new Vector2(dirX, 0f);

        // Jump only if the target is significantly above us (i.e., on a higher platform).
        // This prevents constant bunny-hopping on the same level.
        if (toTarget.Y < -PlatformJumpThreshold && IsOnFloor() && _jumpCooldownRemaining <= 0f)
        {
            AiInput.JumpJustPressed = true;
            _jumpCooldownRemaining = JumpCooldown;
        }
    }

    /// <summary>
    /// Moves directly away from the target (used during retreat phase).
    /// Stops at edges rather than retreating off the stage.
    /// </summary>
    protected void MoveAwayFromTarget(Vector2 toTarget)
    {
        float dirX = -Mathf.Sign(toTarget.X);

        // Don't retreat off an edge — it's better to stand your ground than fall.
        if (IsOnFloor() && IsEdgeAhead(dirX))
        {
            AiInput.MoveDirection = Vector2.Zero;
            return;
        }

        AiInput.MoveDirection = new Vector2(dirX, 0f);
    }

    // ======================================================================
    // Platform / edge awareness
    // ======================================================================

    /// <summary>
    /// Creates a downward RayCast2D child used for edge detection.
    /// The ray is repositioned each frame based on movement direction.
    /// </summary>
    private void SetupEdgeDetection()
    {
        _edgeRay = new RayCast2D
        {
            Name          = "EdgeRay",
            Enabled       = true,
            CollisionMask = 1,                              // world / platform layer
            TargetPosition = new Vector2(0f, EdgeRayDepth) // aims straight down
        };
        AddChild(_edgeRay);
    }

    /// <summary>
    /// Returns true if there is no floor within EdgeRayDepth pixels ahead of the AI
    /// in the given horizontal direction — i.e., an edge or drop is imminent.
    /// </summary>
    private bool IsEdgeAhead(float directionX)
    {
        if (directionX == 0f || _edgeRay == null) return false;

        // Shift the ray's origin to EdgeRayLookAhead pixels ahead in the movement direction.
        _edgeRay.Position = new Vector2(directionX * EdgeRayLookAhead, 0f);
        _edgeRay.ForceRaycastUpdate();
        return !_edgeRay.IsColliding();
    }

    /// <summary>
    /// Fires when the AI has been airborne longer than RecoveryAirborneThreshold,
    /// meaning it has likely been knocked off a platform.
    ///
    /// Priority: character-specific recovery move (e.g. Steampunk up-attack) →
    ///           double-jump toward the target.
    ///
    /// Returns true if a recovery action was taken (caller should skip normal AI).
    /// </summary>
    private bool HandleAirborneRecovery()
    {
        // Only trigger after being airborne longer than a normal jump arc.
        if (_airborneTime < RecoveryAirborneThreshold) return false;

        // Steer toward the target so we land on or near the stage.
        if (Target != null && !Target.IsDead)
            AiInput.MoveDirection = new Vector2(Mathf.Sign(Target.GlobalPosition.X - GlobalPosition.X), 0f);

        // Try the subclass-specific recovery move first (returns false if unavailable).
        if (TryRecoveryMove()) return true;

        // Generic fallback: burn a remaining jump to gain height.
        if (_jumpCooldownRemaining <= 0f)
        {
            AiInput.JumpJustPressed = true;
            _jumpCooldownRemaining = JumpCooldown;
        }
        return true;
    }

    /// <summary>
    /// Override in a subclass to provide a character-specific aerial recovery move
    /// (e.g. an up-attack that also launches the character upward).
    /// Return true if the move was successfully initiated, false if unavailable.
    /// </summary>
    protected virtual bool TryRecoveryMove() => false;

    // ======================================================================
    // Threat detection
    // ======================================================================

    /// <summary>
    /// Programmatically creates an Area2D child that detects incoming hitboxes.
    /// When one enters, OnThreatDetected() fires so the AI can react.
    /// </summary>
    private void SetupDangerZone()
    {
        // Broad mask so we catch hitboxes on whatever layer the project uses.
        // Narrow this if you get false positives (e.g. 0b0001 if hitboxes are on layer 1).
        _dangerZone = new Area2D { Name = "DangerZone", CollisionMask = 0b1111 };

        var shape = new CollisionShape2D
        {
            Shape = new CircleShape2D { Radius = ThreatDetectionRadius }
        };
        _dangerZone.AddChild(shape);
        AddChild(_dangerZone);

        _dangerZone.AreaEntered += OnDangerZoneAreaEntered;
    }

    private void OnDangerZoneAreaEntered(Area2D area)
    {
        // Only care about Hitbox areas that belong to someone else.
        if (area is not Hitbox hitbox) return;
        if (hitbox.OwnerNode == this || hitbox.OwnerNode == null) return;

        // Heuristic: if the hitbox's parent is a fighter (IDamageable) it's a melee
        // attack attached to a body. If the parent is NOT a fighter, it's free-flying
        // (a projectile or thrown object).
        bool isProjectile = hitbox.GetParent() is not IDamageable;

        OnThreatDetected(hitbox, isProjectile);
    }

    /// <summary>
    /// Called when an enemy hitbox or projectile enters the danger zone.
    ///
    /// Default behavior:
    ///   - Projectile within attack range → attack it (despawns it on contact).
    ///   - Otherwise → dodge roll, with a jump as fallback if dodge is on cooldown.
    ///
    /// Override in subclasses for character-specific reactions.
    /// </summary>
    protected virtual void OnThreatDetected(Hitbox threat, bool isProjectile)
    {
        if (IsInvincible || CurrentState == CharacterState.Dead) return;

        Vector2 toThreat = threat.GlobalPosition - GlobalPosition;

        if (isProjectile && IsInAttackRange(toThreat))
        {
            // Bat the projectile away — works because SteampunkProjectile destroys
            // itself on any Hitbox collision (OnHitboxCollision in SteampunkProjectile).
            AiInput.AttackJustPressed = true;
        }
        else
        {
            TryDodgeOrJump(toThreat);
        }
    }

    /// <summary>
    /// Attempts a dodge roll away from the threat.
    /// Falls back to a jump if the dodge is still on cooldown.
    /// </summary>
    private void TryDodgeOrJump(Vector2 toThreat)
    {
        // Point the move direction away from the threat so the horizontal roll
        // carries us in the right direction.
        AiInput.MoveDirection = new Vector2(-Mathf.Sign(toThreat.X), 0f);

        DodgeDirection dodgeDir = Mathf.Abs(toThreat.X) > 30f
            ? DodgeDirection.Horizontal
            : DodgeDirection.Neutral;

        bool dodged = TryStartDodge(dodgeDir);

        // Jump is last resort — used when dodge is still on cooldown.
        if (!dodged && _jumpCooldownRemaining <= 0f)
        {
            AiInput.JumpJustPressed = true;
            _jumpCooldownRemaining = JumpCooldown;
        }
    }

    // ======================================================================
    // Aggressiveness
    // ======================================================================

    private void TickAggressiveness(float delta)
    {
        _aggroModeTimer -= delta;
        if (_aggroModeTimer <= 0f)
        {
            RerollAggressivenessMode();
            _aggroModeTimer = AggroModeChangeInterval;
        }
    }

    /// <summary>
    /// Picks a new aggressiveness mode weighted by current HP:
    ///   Below BerserkHpThreshold → always Berserk.
    ///   Above 75% HP             → lean Cautious/Neutral.
    ///   25–75% HP                → lean Neutral/Aggressive.
    /// </summary>
    private void RerollAggressivenessMode()
    {
        float hpPct = (float)CurrentHP / MaxHP;

        if (hpPct < BerserkHpThreshold)
        {
            CurrentMode = AggressivenessMode.Berserk;
            return;
        }

        double roll = _rng.NextDouble();
        CurrentMode = hpPct > 0.75f
            ? (roll < 0.6 ? AggressivenessMode.Cautious   : AggressivenessMode.Neutral)
            : (roll < 0.5 ? AggressivenessMode.Neutral     : AggressivenessMode.Aggressive);
    }

    /// <summary>
    /// Attack cooldown multiplier applied after each attack.
    /// Lower = attacks more often.
    /// </summary>
    private float GetCooldownMultiplier() => CurrentMode switch
    {
        AggressivenessMode.Cautious   => 1.6f,
        AggressivenessMode.Neutral    => 1.0f,
        AggressivenessMode.Aggressive => 0.7f,
        AggressivenessMode.Berserk    => 0.4f,
        _                              => 1.0f
    };

    /// <summary>
    /// Retreat duration multiplier after each attack.
    /// Lower = barely backs away before engaging again.
    /// </summary>
    private float GetRetreatMultiplier() => CurrentMode switch
    {
        AggressivenessMode.Cautious   => 1.5f,
        AggressivenessMode.Neutral    => 1.0f,
        AggressivenessMode.Aggressive => 0.6f,
        AggressivenessMode.Berserk    => 0.15f,
        _                              => 1.0f
    };

    /// <summary>
    /// Hesitation interval multiplier.
    /// Higher = longer gaps between pauses (hesitates less often).
    /// </summary>
    private float GetHesitationIntervalMultiplier() => CurrentMode switch
    {
        AggressivenessMode.Cautious   => 0.7f,  // pauses more frequently
        AggressivenessMode.Neutral    => 1.0f,
        AggressivenessMode.Aggressive => 1.5f,  // pauses less frequently
        AggressivenessMode.Berserk    => 4.0f,  // barely ever pauses
        _                              => 1.0f
    };

    // ======================================================================
    // Hesitation
    // ======================================================================

    private void TickHesitation(float delta)
    {
        // Count down the active pause.
        if (_hesitationActiveFor > 0f)
        {
            _hesitationActiveFor -= delta;
            return;
        }

        // Count down to the next pause.
        _nextHesitationIn -= delta;
        if (_nextHesitationIn <= 0f)
        {
            // Randomize ±25% so it doesn't feel mechanical.
            _hesitationActiveFor = HesitationDuration * (float)(_rng.NextDouble() * 0.5 + 0.75);

            // Schedule the next pause, scaled by aggressiveness.
            float interval = HesitationMinInterval
                + (float)_rng.NextDouble() * (HesitationMaxInterval - HesitationMinInterval);
            _nextHesitationIn = interval * GetHesitationIntervalMultiplier();
        }
    }

    // ======================================================================
    // Attack selection
    // ======================================================================

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
    /// Picks and executes a matching attack. Returns false if none match.
    /// Applies the aggressiveness cooldown multiplier to the post-attack cooldown.
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

        // Aggressiveness mode scales how quickly the AI can attack again.
        _aiAttackCooldownRemaining = AiAttackCooldown * GetCooldownMultiplier();
        if (selected.IsSpecial)
            _aiSpecialCooldownRemaining = AiSpecialCooldown;
        return true;
    }

    /// <summary>
    /// Returns true if the target is within any currently available attack's range.
    /// Use this to decide whether to wait vs chase.
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
        return angle >= min || angle <= max; // wraps around 360
    }
}
