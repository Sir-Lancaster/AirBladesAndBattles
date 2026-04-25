using Godot;

public abstract partial class CharacterBase : CharacterBody2D, IDamageable
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
    /// Dodge and hit i-frame timers.
    /// </summary>
    [Export] public float DodgeTime = 0.30f;
    [Export] public float DodgeIFrameTime = 0.28f;
    [Export] public float DodgeCooldown = 0.8f;
    [Export] public float HitIFrameTime = 0.25f;

    /// <summary>
    /// Runtime character attributes.
    /// </summary>
    public bool IsDead { get; protected set; }
    [Export] public int CurrentHP { get; set; }
    [Export] public CharacterState CurrentState { get; set; }
    public bool IsInvincible { get; private set; }
    private float _hitStunRemaining;
    private float _dodgeRemaining;
    private float _dodgeCooldownRemaining;
    private float _dodgeVelocityX;

    private int _jumpsRemaining;
    private CharacterState _lastReplicatedState;
    private bool _multiplayerReady;

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

        // Reliably push the new HP to all peers so the HUD updates immediately.
        // The periodic SyncState is unreliable and can miss the exact damage frame.
        if (_multiplayerReady && Multiplayer.MultiplayerPeer != null && IsMultiplayerAuthority())
            Rpc(nameof(SyncHp), CurrentHP);

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

        // Notify remote peers reliably so fast states like Attack are never dropped.
        // _multiplayerReady guards against RPCs during _Ready() initialization.
        if (_multiplayerReady && Multiplayer.MultiplayerPeer != null && IsMultiplayerAuthority())
            Rpc(nameof(SyncStateChange), (int)newState);
    }

    // Reliable so short-lived states (Attack, Dodge) are guaranteed to arrive.
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false,
         TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SyncStateChange(int state)
    {
        CurrentState = (CharacterState)state;
        PlayAnimationForState(CurrentState);
        _lastReplicatedState = CurrentState;
    }

    // Plays an animation by name locally and sends it reliably to all remote peers.
    // Call this from OnAttackPerformed/OnSpecialPerformed instead of SetAnimation/AnimationPlayer.Play.
    protected void BroadcastAnimation(string animName)
    {
        PlayAnimationByName(animName);
        if (_multiplayerReady && Multiplayer.MultiplayerPeer != null && IsMultiplayerAuthority())
            Rpc(nameof(ReceiveAnimSync), animName);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false,
         TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveAnimSync(string animName) => PlayAnimationByName(animName);

    // Virtual hooks to be overridden in individual characters.

    // Lifecycle/State
    protected virtual void OnStateChanged(CharacterState currentState, CharacterState newState) { }
    protected virtual void PlayAnimationForState(CharacterState state) { }
    // Override to delegate to each character's internal SetAnimation / AnimationPlayer.Play.
    protected virtual void PlayAnimationByName(string animName) { }

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
        // Must run before _PhysicsProcess. _Ready is guaranteed to fire after the spawner
        // has set the node name, unlike _EnterTree which can fire before the name is assigned
        // on clients receiving a replicated spawn.
        if (int.TryParse(Name, out int peerId))
            SetMultiplayerAuthority(peerId);

        CurrentHP = MaxHP;
        IsDead = false;
        _jumpsRemaining = MaxJumps;
        CurrentState = CharacterState.Run; // To ensure that SetState fires correcty, set current state to a non-idle value then call Setstate().
        SetState(CharacterState.Idle);     // _multiplayerReady is false here, so no RPC is sent.

        // Layer 2 = characters; mask 1 = world only.
        // Characters pass through each other and through themselves (multi-player safe).
        CollisionLayer = 2;
        CollisionMask = 1;

        // Allow SyncStateChange RPCs only after initialization is complete and the node
        // is fully registered in the scene tree with the multiplayer system.
        _multiplayerReady = true;
    }

    /// <summary>
    /// While a character is in hitstun state, the timer decreases.
    /// </summary>
    /// <param name="delta">delta represents time.</param>
    public override void _PhysicsProcess(double delta)
    {
        if (!IsMultiplayerAuthority())
        {
            // Drive animations from replicated CurrentState on remote clients.
            if (CurrentState != _lastReplicatedState)
            {
                PlayAnimationForState(CurrentState);
                _lastReplicatedState = CurrentState;
            }
            MoveAndSlide(); // apply replicated velocity
            return;         // skip all input and local physics
        }

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

        // Broadcast state to all non-authority peers every physics frame.
        if (Multiplayer.MultiplayerPeer != null)
            Rpc(nameof(SyncState), GlobalPosition, Velocity, (int)CurrentState, CurrentHP);
    }

    // UnreliableOrdered: only the freshest packet matters, old ones are dropped.
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false,
         TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
    private void SyncState(Vector2 pos, Vector2 vel, int state, int hp)
    {
        GlobalPosition = pos;
        Velocity       = vel;
        CurrentState   = (CharacterState)state;
        CurrentHP      = hp;
    }

    // Reliable one-shot sent on every hit so the HUD always reflects damage immediately.
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false,
         TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SyncHp(int hp) => CurrentHP = hp;

    private void HandleCombatInput()
    {
        if (!IsMultiplayerAuthority()) return;
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
            else if (Mathf.Abs(move.X) > 0.3f) dir = SpecialDirection.Neutral;

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
        if (!IsMultiplayerAuthority()) return;
        if (CurrentState == CharacterState.HitStun || CurrentState == CharacterState.Dead)
            return;

        Vector2 move = Input.GetVector("move_left", "move_right", "move_up", "move_down");

        if (CurrentState != CharacterState.Attack && CurrentState != CharacterState.Dodge)
        {
            Velocity = new Vector2(move.X * MoveSpeed, Velocity.Y);

            if (IsOnFloor())
            {
                _jumpsRemaining = MaxJumps;
                if (move.X == 0)
                    SetState(CharacterState.Idle);
                else
                    SetState(CharacterState.Run);
            }
        }

        if (Input.IsActionJustPressed("jump") && _jumpsRemaining > 0 && CurrentState != CharacterState.Attack)
        {
                _jumpsRemaining--;
                Velocity = new Vector2(Velocity.X, JumpVelocity);
                SetState(CharacterState.Jump);
        }
    }

    // Called on this character's authority peer by a remote attacker's Hitbox.
    // Reliable transfer ensures damage is never silently dropped.
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
         TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceiveHitRpc(int damage)
    {
        if (!IsMultiplayerAuthority()) return;
        TakeDamage(damage);
    }

    // Called by a remote attacker's LassoHandler when a grab arc begins.
    // Stops this peer's own physics so it doesn't fight the arc positioning.
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
         TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void FreezeForLasso()
    {
        if (!IsMultiplayerAuthority()) return;
        SetPhysicsProcess(false);
    }

    // Called when the grab arc completes to restore normal physics.
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
         TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void UnfreezeForLasso()
    {
        if (!IsMultiplayerAuthority()) return;
        SetPhysicsProcess(true);
    }

    // Per-frame arc position sent by the attacker's LassoHandler.
    // Applies locally and re-broadcasts via SyncState so all peers see the throw.
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
         TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
    public void LassoPositionUpdate(Vector2 pos)
    {
        if (!IsMultiplayerAuthority()) return;
        GlobalPosition = pos;
        if (Multiplayer.MultiplayerPeer != null)
            Rpc(nameof(SyncState), GlobalPosition, Velocity, (int)CurrentState, CurrentHP);
    }
}
