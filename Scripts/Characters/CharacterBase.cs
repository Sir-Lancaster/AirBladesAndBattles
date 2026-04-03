using Godot;

public abstract partial class CharacterBase : CharacterBody2D
{
    /// <summary>
    /// Enums include the characters states, the possible attack directions,
    /// and the possible special directions.
    /// </summary>
    public enum CharacterState { Idle, Run, Jump, Dodge, Attack, HitStun, Dead }
    public enum AttackDirection { Horizontal, Up, DownAir }
    public enum SpecialDirection { Neutral, Up, Horizontal }
    public enum DodgeDirection { Neutral, Horizontal }

    /// <summary>
    /// Core attributes for a character (HP, Speed, Jump height, Damages, State).
    /// </summary>
    [Export] public int MaxHP = 100;
    [Export] public float MoveSpeed = 200f;
    [Export] public float JumpVelocity = -420f;
    [Export] public float Gravity = 900f;
    [Export] public int BasicDamage = 4;
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

    // Base methods, owned by the core class.

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
            float inputX    = Input.GetVector("move_left", "move_right", "move_up", "move_down").X;
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

    // Basic Godot Overrides

    /// <summary>
    /// Sets the character's HP to the max, IsDead to false, and puts the character into the idle state.
    /// </summary>
    public override void _Ready()
    {
        CurrentHP = MaxHP;
        IsDead = false;
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

        Vector2 move = Input.GetVector("move_left", "move_right", "move_up", "move_down");

        if (Input.IsActionJustPressed("attack"))
        {
            AttackDirection dir = AttackDirection.Horizontal;
            if (move.Y < -0.5f) dir = AttackDirection.Up;
            else if (move.Y > 0.5f && !IsOnFloor()) dir = AttackDirection.DownAir;

            PerformAttack(dir);
        }

        if (Input.IsActionJustPressed("special"))
        {
            SpecialDirection dir = SpecialDirection.Neutral;
            if (move.Y < -0.5f) dir = SpecialDirection.Up;
            else if (Mathf.Abs(move.X) > 0.3f) dir = SpecialDirection.Horizontal;

            PerformSpecial(dir);
        }

        if (Input.IsActionJustPressed("dodge"))
        {
            DodgeDirection dir = Mathf.Abs(move.X) > 0.3f ? DodgeDirection.Horizontal : DodgeDirection.Neutral;
            TryStartDodge(dir);
        }
    }

    private void HandleMovementInput()
    {
        if (CurrentState == CharacterState.HitStun || CurrentState == CharacterState.Dead)
            return;

        Vector2 move = Input.GetVector("move_left", "move_right", "move_up", "move_down");

        if (CurrentState != CharacterState.Attack && CurrentState != CharacterState.Dodge)
        {
            Velocity = new Vector2(move.X * MoveSpeed, Velocity.Y);

            if (IsOnFloor())
            {
                if (move.X == 0)
                    SetState(CharacterState.Idle);
                else
                    SetState(CharacterState.Run);
            }
        }

        if (Input.IsActionJustPressed("jump") && IsOnFloor())
        {
            Velocity = new Vector2(Velocity.X, JumpVelocity);
            SetState(CharacterState.Jump);
        }
    }
}