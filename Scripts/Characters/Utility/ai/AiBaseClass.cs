using System;
using System.Collections.Generic;
using Godot;

public abstract partial class AiBaseClass : CharacterBody2D, IDamageable
{
    /// <summary>
    /// Enums include the characters states, the possible attack directions,
    /// and the possible special directions.
    /// </summary>
    public enum CharacterState { Idle, Run, Jump, Dodge, Attack, HitStun, Dead }
    public enum AttackDirection { Horizontal, Up, DownAir }
    public enum SpecialDirection { Neutral, Up, horizontal } // Horizontal will not be added for the school project, but may be implemented later.
    public enum DodgeDirection { Neutral, Horizontal }

    /// <summary>
    /// Core attributes for a character (HP, Speed, Jump height, Damages, State).
    /// </summary>
    [Export] public int MaxHP = 100;
    [Export] public float MoveSpeed = 200f;
    [Export] public float JumpVelocity = -420f;
    [Export] public float Gravity = 900f;
    [Export] public int BasicDamage = 4;
    [Export] public int MaxJumps = 2;
    public int SpecialDamage => BasicDamage * 2;

    /// <summary>
    /// Dodge and hitstun timers.
    /// </summary>
    [Export] public float DodgeTime = 0.30f;
    [Export] public float DodgeIFrameTime = 0.28f;
    [Export] public float DodgeCooldown = 0.8f;
    [Export] public float HitStunTimer = 0.25f;

    /// <summary>
    /// Runtime character attributes.
    /// </summary>
    public bool IsDead { get; protected set; }
    public int CurrentHP { get; protected set; }
    public CharacterState CurrentState { get; protected set; }
    public bool IsInvincible { get; private set; }
    private float _hitStunRemaining;
    private float _dodgeRemaining;
    private float _dodgeCooldownRemaining;
    private float _dodgeVelocityX;
    private float _aiAttackCooldownRemaining;

    [Export] public float AiAttackCooldown = 1.0f;

    private int _jumpsRemaining;

    // Base methods, owned by the core class.

    /// <summary>
    /// Called by Hitbox via reflection when its area overlaps this character's hurtbox.
    /// Rejects the hit if the attacker is self, or if the character is dead/invincible.
    /// Otherwise applies damage and returns true so the hitbox records the hit.
    /// </summary>
    /// <param name="attacker">The node that owns the hitbox.</param>
    /// <param name="hitbox">The hitbox that made contact.</param>
    /// <param name="damage">Damage amount to apply.</param>
    /// <returns>True if the hit was accepted and damage applied; false otherwise.</returns>
    public bool TryReceiveHit(Node attacker, Hitbox _hitbox, int damage)
    {
        if (attacker == this) return false;
        if (IsDead || IsInvincible) return false;

        TakeDamage(damage);
        return true;
    }



    /// <summary>
    /// TakeDamage() chacks that the character isn't dead or it returns early.
    /// It records the old hp, then calculates and saves the new HP into currentHp. 
    /// Resetting it to 0 if the damage would put current into the negative.
    /// It then calls the appropriate virtual hooks. Then it checks to see if the damage killed the character.
    /// Finally, it sets the hitstun timer and calls SetState with Hitstun as the new state.
    /// </summary>
    /// <param name="amount">amount is an integer containing the damage value the character will take.</param>
    public void TakeDamage(int amount)
    {
        if (IsDead || amount <= 0 || IsInvincible) return;

        int oldHp = CurrentHP;
        CurrentHP = Mathf.Max(0, CurrentHP - amount);

        OnHealthChanged(oldHp, CurrentHP);
        OnDamaged(amount);

        if (CurrentHP == 0)
        {
            IsDead = true;
            SetState(CharacterState.Dead);
            OnDied();
            return;
        }

        _hitStunRemaining = HitStunTimer;
        SetState(CharacterState.HitStun);
    }

    /// <summary>
    /// Attempts to start a dodge if the character is allowed to dodge right now.
    /// Blocks during locked states and while the cooldown is active.
    /// For a horizontal dodge the roll velocity is captured from the current input.
    /// Starts the iFrame window, transitions to the Dodge state, and fires OnDodgeStarted.
    /// </summary>
    /// <param name="direction">Neutral for a spot dodge; Horizontal for a roll.</param>
    /// <returns>True when dodge starts successfully; otherwise false.</returns>
    public bool TryStartDodge(DodgeDirection direction)
    {
        if (CurrentState == CharacterState.HitStun ||
            CurrentState == CharacterState.Dead  ||
            CurrentState == CharacterState.Dodge ||
            CurrentState == CharacterState.Attack)
            return false;

        if (_dodgeCooldownRemaining > 0) return false;

        _dodgeRemaining  = DodgeTime;
        _dodgeVelocityX  = 0f;

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

    /// <summary>
    /// Routes a normal attack by direction and triggers attack hooks for character-specific behavior.
    /// </summary>
    /// <param name="direction">Requested normal attack direction.</param>
public void PerformAttack(AttackDirection direction)
    {
        // Block attacks during locked states.
        if (CurrentState == CharacterState.HitStun ||
            CurrentState == CharacterState.Dead ||
            CurrentState == CharacterState.Dodge ||
            CurrentState == CharacterState.Attack)
        {
            return;
        }

        // Down-air only makes sense while airborne.
        AttackDirection resolvedDirection = direction;
        if (resolvedDirection == AttackDirection.DownAir && IsOnFloor())
            resolvedDirection = AttackDirection.Horizontal;

        Velocity = new Vector2(0f, Velocity.Y);
        SetState(CharacterState.Attack);
        OnAttackPerformed(resolvedDirection, BasicDamage);
    }

    /// <summary>
    /// Routes a special attack by direction and triggers special hooks for character-specific behavior.
    /// </summary>
    /// <param name="direction">Requested special attack direction.</param>
    public void PerformSpecial(SpecialDirection direction)
    {
        // Block specials during locked states.
        if (CurrentState == CharacterState.HitStun ||
            CurrentState == CharacterState.Dead ||
            CurrentState == CharacterState.Dodge ||
            CurrentState == CharacterState.Attack)
        {
            return;
        }
        Velocity = new Vector2(0f, Velocity.Y);
        SpecialDirection resolvedDirection = direction;
        SetState(CharacterState.Attack);
        OnSpecialPerformed(resolvedDirection, SpecialDamage);
    }

    /// <summary>
    /// SetState changes a character into a new state. Fist it contains a check to prevent changing
    /// to a new state. Then it calls the virtual hook, changes the character state, and calls the 
    /// virtual hook to play the animation for the state.
    /// </summary>
    /// <param name="newState">Contains the state the character is going to switch into.</param>
    protected void SetState(CharacterState newState)
    {
        if (CurrentState == newState) return;

        OnStateChanged(CurrentState, newState);
        CurrentState = newState;
        PlayAnimationForState(newState);
    }

    // Virtual hooks to be overridden in individual characters.

    // Lifecycle/State
    protected virtual void OnStateChanged(CharacterState currentState, CharacterState newState) { }
    protected virtual void PlayAnimationForState(CharacterState state) { }

    // Health
    protected virtual void OnHealthChanged(int oldHp, int newHp) { }
    protected virtual void OnDamaged(int amount) { }
    protected virtual void OnDied() { }

    // Combat
    protected virtual void OnAttackPerformed(AttackDirection direction, int damage) { }
    protected virtual void OnSpecialPerformed(SpecialDirection direction, int damage) { }

    // Dodge
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

    // Basic Godot Overrides

    /// <summary>
    /// Sets the character's HP to the max, IsDead to false, and puts the character into the idle state.
    /// </summary>
    public override void _Ready()
    {
        CurrentHP = MaxHP;
        IsDead = false;
        _jumpsRemaining = MaxJumps;
        CurrentState = CharacterState.Run; // To ensure that SetState fires correcty, set current state to a non-idle value then call Setstate().
        SetState(CharacterState.Idle);
    }

    /// <summary>
    /// While a character is in hitstun state, the timer decreases.
    /// </summary>
    /// <param name="delta">delta represents time.</param>
    public override void _PhysicsProcess(double delta)
    {
        if (IsDead) return;

        if (!IsOnFloor())
        {
            Velocity += new Vector2(0, Gravity * (float)delta);
        }

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

        if (_dodgeCooldownRemaining > 0)
            _dodgeCooldownRemaining -= (float)delta;

        if (_aiAttackCooldownRemaining > 0)
            _aiAttackCooldownRemaining -= (float)delta;

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
            SpecialDirection dir = SpecialDirection.Neutral;
            if (AiInput.MoveDirection.Y < -0.5f) dir = SpecialDirection.Up;
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
                if (AiInput.MoveDirection.X == 0)
                    SetState(CharacterState.Idle);
                else
                    SetState(CharacterState.Run);
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
        public float MinDist, MaxDist;   // world units; use 0/float.MaxValue for "any distance"
        public Action Execute;
    }

    private readonly List<AttackOption> _attackOptions = new();
    private readonly System.Random _rng = new();

    /// <summary>
    /// Registers an attack the AI can use when the target falls within the given angle and distance range.
    /// Multiple overlapping entries are resolved by random selection.
    /// Angles are in degrees, 0-360 (0=right, 90=down, 180=left, 270=up).
    /// Ranges that wrap around 360 are supported (e.g. MinAngle=315, MaxAngle=45 covers "to the right").
    /// </summary>
    protected void RegisterAttack(float minAngle, float maxAngle, float minDist, float maxDist, Action execute)
    {
        _attackOptions.Add(new AttackOption
        {
            MinAngle = minAngle, MaxAngle = maxAngle,
            MinDist = minDist,   MaxDist = maxDist,
            Execute = execute
        });
    }

    /// <summary>
    /// Evaluates all registered attacks against the vector to the target.
    /// If one or more match, picks one at random and executes it, returning true.
    /// If nothing matches, returns false — the caller should fall back to MoveTowardTarget.
    /// Only considers attacks when the character is in a state that allows attacking.
    /// </summary>
    protected bool TrySelectAttack(Vector2 toTarget)
    {
        if (CurrentState == CharacterState.Attack ||
            CurrentState == CharacterState.HitStun ||
            CurrentState == CharacterState.Dead)
            return false;

        if (_aiAttackCooldownRemaining > 0) return false;

        float angle = Mathf.RadToDeg(Mathf.Atan2(toTarget.Y, toTarget.X));
        if (angle < 0f) angle += 360f;
        float dist = toTarget.Length();

        var matches = new System.Collections.Generic.List<AttackOption>();
        foreach (var opt in _attackOptions)
        {
            if (AngleInRange(angle, opt.MinAngle, opt.MaxAngle) && dist >= opt.MinDist && dist <= opt.MaxDist)
                matches.Add(opt);
        }

        if (matches.Count == 0) return false;

        matches[_rng.Next(matches.Count)].Execute();
        _aiAttackCooldownRemaining = AiAttackCooldown;
        return true;
    }

    /// <summary>
    /// Fallback movement when no attack matches. Walks toward the target horizontally
    /// and jumps if the target is significantly above.
    /// </summary>
    protected void MoveTowardTarget(Vector2 toTarget)
    {
        AiInput.MoveDirection = new Vector2(Mathf.Sign(toTarget.X), 0f);
        if (toTarget.Y < -80f)
            AiInput.JumpJustPressed = true;
    }

    private static bool AngleInRange(float angle, float min, float max)
    {
        if (min <= max) return angle >= min && angle <= max;
        return angle >= min || angle <= max; // wraps around 360
    }
}
