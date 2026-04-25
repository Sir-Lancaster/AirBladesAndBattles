using System;
using Godot;

/// <summary>
/// Handles all three lasso-based moves for KernelCowboy.
///   LaunchLasso()          — neutral special: snare enemy with lasso, arc over head, slam.
///   LaunchDownAirLasso()   — down air: lasso downward, pull self to target, stomp.
///   LaunchRecoveryLasso()  — special up: hook above, launch self upward.
/// Attach as a child node of KernelCowboy in the scene.
///
/// Visual setup (set these in the Godot editor):
///   HandAnchor  — Marker2D placed at the character's wrist/hand in the scene
///   LassoHead   — Sprite2D (your lasso loop tip sprite), child of the scene root
///   LassoRope   — Line2D with at least 3 points, child of the scene root
/// </summary>
public partial class LassoHandler : Node
{
    // ── Neutral special exports ───────────────────────────────────────────────

    /// <summary>Speed the neutral lasso travels horizontally (px/sec).</summary>
    [Export] public float LassoSpeed = 400f;

    /// <summary>Max horizontal distance before the lasso retracts.</summary>
    [Export] public float LassoRange = 250f;

    /// <summary>Peak height of the over-head arc. Should be at least 2x the tallest character's height.</summary>
    [Export] public float ArcHeight = 160f;

    /// <summary>Seconds to complete the full arc.</summary>
    [Export] public float ArcDuration = 0.6f;

    /// <summary>Speed multiplier applied after the arc peak (arcT > 0.5). 1 = constant, higher = faster descent. 3 is a good starting point with the ease-in curve.</summary>
    [Export] public float SlamSpeedMultiplier = 3.0f;

    /// <summary>Damage on slam landing (dealt directly to the grabbed target).</summary>
    [Export] public int SlamDamage = 20;

    /// <summary>Damage dealt to bystanders caught in the slam splash hitbox.</summary>
    [Export] public int SlamSplashDamage = 10;

    /// <summary>How far behind the attacker the lasso target lands.</summary>
    [Export] public float LandingOffset = 80f; //tune to character size/art

    /// <summary>Collision radius of the neutral lasso tip.</summary>
    [Export] public float LassoRadius = 20f; //tune to lasso art

    // ── Down air exports ──────────────────────────────────────────────────────

    /// <summary>Speed the down-air lasso travels downward (px/sec).</summary>
    [Export] public float DownAirLassoSpeed = 600f;

    /// <summary>Max downward distance before the lasso gives up and fast-falls.</summary>
    [Export] public float DownAirLassoRange = 300f;

    /// <summary>Speed the attacker is pulled toward the floor hook point (px/sec).</summary>
    [Export] public float DownAirPullSpeed = 900f;

    /// <summary>Damage dealt by the stomp hitbox on landing.</summary>
    [Export] public int StompDamage = 15;

    /// <summary>Collision radius of the down-air lasso tip.</summary>
    [Export] public float DownAirLassoRadius = 20f; //tune to lasso art

    /// <summary>Damage dealt to enemies caught in the shockwave (not directly stomped).</summary>
    [Export] public int ShockwaveDamage = 6;

    /// <summary>How far to each side the shockwave extends from the landing point (px).</summary>
    [Export] public float ShockwaveWidth = 100f; //tune to feel

    /// <summary>Height of the shockwave hitbox.</summary>
    [Export] public float ShockwaveHeight = 30f; //tune to feel

    /// <summary>How long the shockwave hitbox lives in seconds.</summary>
    [Export] public float ShockwaveLifetime = 0.12f;

    // ── Recovery lasso exports ────────────────────────────────────────────────

    /// <summary>How far above the character the hook point is set when recovery is used.</summary>
    [Export] public float RecoveryLassoLength = 350f;

    /// <summary>Speed the lasso head travels upward toward the hook point (px/sec).</summary>
    [Export] public float RecoveryLassoSpeed = 800f;

    /// <summary>Upward velocity applied to the character when the lasso hooks.</summary>
    [Export] public float RecoveryLaunchSpeed = 650f;

    /// <summary>How close the character needs to get to the hook point before the rope releases (px).</summary>
    [Export] public float RecoveryArrivalThreshold = 60f;

    // ── Visual exports ────────────────────────────────────────────────────────

    /// <summary>
    /// Marker2D placed at the character's hand/wrist in the scene.
    /// The rope Line2D starts from this point.
    /// </summary>
    [Export] public Marker2D HandAnchor;

    /// <summary>
    /// Node2D representing the lasso loop/hook tip.
    /// Moves to match the lasso head position each frame.
    /// </summary>
    [Export] public Node2D LassoHead;

    /// <summary>
    /// The AnimatedSprite2D inside LassoHead that plays the hook animation.
    /// Assign in the Inspector to the AnimatedSprite2D child of your LassoHead node.
    /// </summary>
    [Export] public AnimatedSprite2D LassoHeadSprite;

    /// <summary>
    /// Name of the animation to play on LassoHeadSprite when the lasso connects or hooks.
    /// Must match the animation name in the SpriteFrames resource.
    /// </summary>
    [Export] public string LassoHitAnimation = "Hit"; //set to your animation name

    /// <summary>
    /// Line2D used to draw the rope. Needs exactly 3 points (set up in editor or created here).
    /// Point 0 = anchor, Point 1 = midpoint (sag), Point 2 = lasso head.
    /// </summary>
    [Export] public Line2D LassoRope;

    /// <summary>Max downward sag of the rope at its midpoint (px). Fades to 0 when taut.</summary>
    [Export] public float RopeSag = 25f;

    // ── Callbacks ─────────────────────────────────────────────────────────────

    /// <summary>Fired when the neutral slam lands.</summary>
    public Action OnSlamComplete;

    /// <summary>Fired at the slam landing position. Spawn a splash hitbox here to damage bystanders.</summary>
    public Action<Vector2> OnSlamSplash;

    /// <summary>Fired when the neutral lasso retracts without a hit.</summary>
    public Action OnLassoMissed;

    /// <summary>Fired the moment the neutral lasso snares a target (before the arc begins).</summary>
    public Action OnLassoConnected;

    /// <summary>Fired the moment the lasso hooks the floor. Spawn the stomp hitbox here.</summary>
    public Action<Vector2> OnFloorHooked;

    /// <summary>Fired when the character body arrives at the floor. Spawn shockwave hitboxes here.</summary>
    public Action<Vector2> OnStompLanded;

    /// <summary>Fired when the down-air stomp lands or the lasso misses.</summary>
    public Action OnDownAirComplete;

    /// <summary>Fired immediately after the recovery lasso launches the owner.</summary>
    public Action OnRecoveryComplete;

    // ── Public state ──────────────────────────────────────────────────────────

    /// <summary>True while the neutral lasso arc is in progress.</summary>
    public bool IsLassoing { get; private set; }

    // ── Private state ─────────────────────────────────────────────────────────

    private CharacterBody2D _owner;

    // Neutral special
    private bool _lassoActive;
    private Area2D _lassoArea;
    private float _lassoTraveled;
    private Vector2 _lassoDirection;
    private CharacterBody2D _lassoTarget;
    private float _arcT;
    private Vector2 _arcStart;
    private Vector2 _arcEnd;

    // Down air
    private bool _downAirActive;
    private Area2D _downAirArea;
    private float _downAirTraveled;
    private bool _downAirPulling;
    private float _downAirPullElapsed;
    private Vector2 _downAirTarget;
    private bool _shockwaveFired;

    // Recovery
    private bool _recoveryActive;   // lasso head is traveling upward
    private bool _recoveryPulling;  // character has been launched, rope stays visible
    private bool _recoveryUsed;     // true after recovery fires; resets only on landing
    private bool _prevOnFloor;      // floor state last frame, used to detect the landing transition
    private Vector2 _recoveryHookPoint;

    /// <summary>True when the recovery lasso is available to use.</summary>
    public bool IsRecoveryAvailable => !_recoveryUsed && !_recoveryActive && !_recoveryPulling;

    // Visual
    private Vector2 _headPos;      // world-space position of the lasso tip this frame
    private bool _ropeVisible;     // whether to draw the rope at all

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        _owner = GetParent<CharacterBody2D>();

        // Ensure the Line2D has exactly 3 points so we can index them directly.
        if (LassoRope != null)
        {
            while (LassoRope.GetPointCount() < 3)
                LassoRope.AddPoint(Vector2.Zero);
            LassoRope.Visible = false;
        }

        if (LassoHead != null)
            LassoHead.Visible = false;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        // Reset recovery charge on the exact frame the character lands (off-floor → on-floor transition).
        // This fires once per landing instead of every frame on the floor, preventing spurious resets.
        bool onFloor = _owner.IsOnFloor();
        if (_recoveryUsed && !_prevOnFloor && onFloor)
            _recoveryUsed = false;
        _prevOnFloor = onFloor;

        TickNeutralLasso(dt);
        TickNeutralArc(dt);
        TickDownAirLasso(dt);
        TickDownAirPull(dt);
        TickRecoveryLasso(dt);
        TickRecoveryPull();
        UpdateRopeVisual();
    }

    // ── Neutral special ───────────────────────────────────────────────────────

    /// <summary>
    /// Launches the horizontal lasso. facing: +1 right, -1 left.
    /// </summary>
    public void LaunchLasso(float facing)
    {
        if (_lassoActive || IsLassoing) return;

        _lassoDirection = new Vector2(facing, 0f);
        _lassoTraveled = 0f;
        _lassoActive = true;

        _lassoArea = CreateArea(LassoRadius, collisionMask: 2); // layer 2 = characters
        _lassoArea.BodyEntered += OnNeutralBodyEntered;
        _owner.AddChild(_lassoArea);
        _lassoArea.GlobalPosition = _owner.GlobalPosition;

        _headPos = AnchorPos();
        _ropeVisible = true;
        ShowFirstFrame();

        if (Multiplayer.MultiplayerPeer != null && _owner.IsMultiplayerAuthority())
            Rpc(nameof(RpcLaunchLasso), facing);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false,
         TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RpcLaunchLasso(float facing)
    {
        _lassoDirection = new Vector2(facing, 0f);
        _lassoTraveled = 0f;
        _lassoActive = true; // no Area2D on remote peers — visual simulation only
        _headPos = AnchorPos();
        _ropeVisible = true;
        ShowFirstFrame();
    }

    private void TickNeutralLasso(float dt)
    {
        if (!_lassoActive) return;

        float move = LassoSpeed * dt;
        _lassoTraveled += move;

        if (_lassoArea != null && IsInstanceValid(_lassoArea))
        {
            _lassoArea.GlobalPosition += _lassoDirection * move;
            _headPos = _lassoArea.GlobalPosition;
        }
        else
        {
            // Remote peer: no Area2D — simulate head position for the rope visual.
            _headPos = _owner.GlobalPosition + _lassoDirection * _lassoTraveled;
        }

        if (_lassoTraveled >= LassoRange)
        {
            FreeArea(ref _lassoArea);
            _lassoActive = false;
            _ropeVisible = false;
            if (_owner.IsMultiplayerAuthority())
                OnLassoMissed?.Invoke();
        }
    }

    private void TickNeutralArc(float dt)
    {
        if (!IsLassoing) return;

        bool isAuthority = _owner.IsMultiplayerAuthority();

        // On authority, abort if the grabbed target disappeared (freed mid-match).
        if (isAuthority && !IsInstanceValid(_lassoTarget))
        {
            IsLassoing = false;
            _ropeVisible = false;
            return;
        }

        float speed = _arcT >= 0.5f ? SlamSpeedMultiplier : 1.0f;
        _arcT = Mathf.Min(_arcT + dt / ArcDuration * speed, 1f);

        float x = Mathf.Lerp(_arcStart.X, _arcEnd.X, _arcT);

        // Rise (0→0.5): smooth sine arc up to the peak.
        // Fall (0.5→1): quadratic ease-in so the target accelerates and slams abruptly into the ground.
        float arcOffset;
        if (_arcT <= 0.5f)
            arcOffset = Mathf.Sin(_arcT * Mathf.Pi) * ArcHeight;
        else
        {
            float fallT = (_arcT - 0.5f) * 2f;
            arcOffset = (1f - fallT * fallT) * ArcHeight;
        }

        Vector2 arcPos = new Vector2(x, Mathf.Lerp(_arcStart.Y, _arcEnd.Y, _arcT) - arcOffset);

        if (isAuthority)
        {
            _lassoTarget.GlobalPosition = arcPos;
            _headPos = arcPos;

            // Push arc position to the target's authority so it re-broadcasts via SyncState.
            if (Multiplayer.MultiplayerPeer != null && _lassoTarget is CharacterBase targetCb)
            {
                long targetAuth = targetCb.GetMultiplayerAuthority();
                if (targetAuth != Multiplayer.GetUniqueId())
                    targetCb.RpcId(targetAuth, nameof(CharacterBase.LassoPositionUpdate), arcPos);
            }
        }
        else
        {
            // Remote peer: just drive the rope visual — target position is synced via SyncState.
            _headPos = arcPos;
        }

        if (_arcT >= 1f)
        {
            if (isAuthority)
                ExecuteSlam();
            else
            {
                IsLassoing = false;
                _ropeVisible = false;
            }
        }
    }

    private void OnNeutralBodyEntered(Node2D body)
    {
        if (body is not CharacterBody2D target || target is not IDamageable || target == _owner) return;

        FreeArea(ref _lassoArea);
        _lassoActive = false;

        _lassoTarget = target;
        IsLassoing = true;
        _arcT = 0f;
        _arcStart = target.GlobalPosition;
        _arcEnd = _owner.GlobalPosition + new Vector2(-_lassoDirection.X * LandingOffset, 0f);

        // Freeze the target locally (stops MoveAndSlide on this peer's replica).
        _lassoTarget.SetPhysicsProcess(false);

        // Also freeze the target on its own authority so it stops sending SyncState
        // (which would otherwise override KC's arc positioning every frame).
        if (Multiplayer.MultiplayerPeer != null && target is CharacterBase targetCb)
        {
            long targetAuth = targetCb.GetMultiplayerAuthority();
            if (targetAuth != Multiplayer.GetUniqueId())
                targetCb.RpcId(targetAuth, nameof(CharacterBase.FreezeForLasso));
        }

        PlayHitAnimation();
        OnLassoConnected?.Invoke();

        // Sync arc parameters to remote peers so the rope visual follows the throw.
        if (Multiplayer.MultiplayerPeer != null && _owner.IsMultiplayerAuthority())
            Rpc(nameof(RpcLassoArcStart), _arcStart, _arcEnd);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false,
         TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RpcLassoArcStart(Vector2 arcStart, Vector2 arcEnd)
    {
        _lassoActive = false;
        IsLassoing = true;
        _arcT = 0f;
        _arcStart = arcStart;
        _arcEnd = arcEnd;
        _headPos = arcStart;
        PlayHitAnimation();
    }

    private void ExecuteSlam()
    {
        IsLassoing = false;
        _ropeVisible = false;

        if (_lassoTarget != null && IsInstanceValid(_lassoTarget))
        {
            Vector2 landingPos = _lassoTarget.GlobalPosition;

            // Re-enable physics on the replica on this peer.
            _lassoTarget.SetPhysicsProcess(true);

            // Also unfreeze the target on its own authority so SyncState resumes.
            if (Multiplayer.MultiplayerPeer != null && _lassoTarget is CharacterBase unfreezeTarget)
            {
                long targetAuth = unfreezeTarget.GetMultiplayerAuthority();
                if (targetAuth != Multiplayer.GetUniqueId())
                    unfreezeTarget.RpcId(targetAuth, nameof(CharacterBase.UnfreezeForLasso));
            }

            if (_lassoTarget is CharacterBase cb) cb.TakeDamage(SlamDamage);
            else if (_lassoTarget is AiBaseClass ai) ai.TakeDamage(SlamDamage);

            OnSlamSplash?.Invoke(landingPos);
        }
        _lassoTarget = null;
        OnSlamComplete?.Invoke();
    }

    // ── Down air ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Launches the lasso straight down. Pulls the attacker to whatever it hits,
    /// then stomps. If nothing is in range the attacker fast-falls instead.
    /// </summary>
    public void LaunchDownAirLasso()
    {
        if (_downAirActive || _downAirPulling) return;

        GD.Print("DownAir: launched");
        _downAirTraveled = 0f;
        _downAirPullElapsed = 0f;
        _shockwaveFired = false;
        _downAirActive = true;
        _downAirArea = CreateArea(DownAirLassoRadius);
        _downAirArea.BodyEntered += OnDownAirBodyEntered;
        _owner.AddChild(_downAirArea);
        _downAirArea.GlobalPosition = _owner.GlobalPosition;

        _headPos = AnchorPos();
        _ropeVisible = true;
        ShowFirstFrame();
    }

    private void TickDownAirLasso(float dt)
    {
        if (!_downAirActive || !IsInstanceValid(_downAirArea)) return;

        float move = DownAirLassoSpeed * dt;
        _downAirTraveled += move;
        _downAirArea.GlobalPosition += Vector2.Down * move;

        _headPos = _downAirArea.GlobalPosition;

        if (_downAirTraveled >= DownAirLassoRange)
        {
            GD.Print("DownAir: max range — pulling to furthest point");
            _downAirTarget = _downAirArea.GlobalPosition;
            FreeArea(ref _downAirArea);
            _downAirActive = false;
            _downAirPulling = true;

            PlayHitAnimation();
            OnFloorHooked?.Invoke(_downAirTarget);
        }
    }

    private void TickDownAirPull(float dt)
    {
        if (!_downAirPulling) return;

        _downAirPullElapsed += dt;

        Vector2 toTarget = _downAirTarget - _owner.GlobalPosition;
        float step = DownAirPullSpeed * dt;

        // Rope head stays fixed at the hook point while the owner flies to it.
        _headPos = _downAirTarget;

        bool arrived  = toTarget.Length() <= step;
        bool timedOut = _downAirPullElapsed >= 1.5f;
        bool landed   = _owner.IsOnFloor();

        if (landed || arrived || timedOut)
        {
            if (arrived)
                _owner.GlobalPosition = _downAirTarget;

            _downAirPulling = false;
            _downAirPullElapsed = 0f;
            _ropeVisible = false;

            // Shockwave and attack-end fire together the instant the character touches down.
            if (!_shockwaveFired)
            {
                _shockwaveFired = true;
                OnStompLanded?.Invoke(_owner.GlobalPosition);
            }

            OnDownAirComplete?.Invoke();
        }
        else
        {
            // Zero velocity so MoveAndSlide doesn't fight our direct position update.
            _owner.Velocity = Vector2.Zero;
            _owner.GlobalPosition += toTarget.Normalized() * step;
        }
    }

    private void OnDownAirBodyEntered(Node2D body)
    {
        GD.Print($"DownAir: body entered — {body.Name} ({body.GetType().Name})");
        if (!_downAirActive || body == _owner) return;

        // Ignore character bodies — we only hook the floor/terrain.
        if (body is IDamageable) return;

        // Use the lasso tip's current position (at the floor surface), not the body center.
        // body.GlobalPosition is the center of the floor tile which can be far underground.
        Vector2 contactPoint = _downAirArea.GlobalPosition;
        GD.Print($"DownAir: hooked floor — pulling to {contactPoint}");
        FreeArea(ref _downAirArea);
        _downAirActive = false;

        _downAirTarget = contactPoint;
        _downAirPulling = true;

        PlayHitAnimation();

        // Defer the callback so KernelCowboy's AddChild (spawning the stomp hitbox) doesn't
        // fire inside a BodyEntered physics callback — Godot forbids that during query flushing.
        Callable.From(() => OnFloorHooked?.Invoke(_downAirTarget)).CallDeferred();
    }

    // ── Recovery lasso (special up) ───────────────────────────────────────────

    /// <summary>
    /// Shoots the lasso upward. The character stays in Attack state while the lasso
    /// travels. When the head reaches the hook point: play hit animation, launch the
    /// character upward, and end the Attack state so the player can steer.
    /// The rope stays visible until the character arrives near the hook or starts falling.
    /// </summary>
    /// <returns>True if the recovery launched successfully; false if it was blocked (already used, in progress).</returns>
    public bool LaunchRecoveryLasso()
    {
        if (_recoveryActive || _recoveryPulling || _recoveryUsed) return false;

        _recoveryUsed = true;
        _recoveryHookPoint = _owner.GlobalPosition + Vector2.Up * RecoveryLassoLength;
        _recoveryActive = true;

        _headPos = AnchorPos();
        _ropeVisible = true;
        ShowFirstFrame();
        return true;
    }

    private void TickRecoveryLasso(float dt)
    {
        if (!_recoveryActive) return;

        float move = RecoveryLassoSpeed * dt;
        Vector2 toHook = _recoveryHookPoint - _headPos;

        if (toHook.Length() <= move)
        {
            // Lasso reached the hook point.
            _headPos = _recoveryHookPoint;
            _recoveryActive = false;
            _recoveryPulling = true;

            PlayHitAnimation();

            // Launch the character and end the Attack state so they can steer.
            _owner.Velocity = new Vector2(_owner.Velocity.X, -RecoveryLaunchSpeed);
            OnRecoveryComplete?.Invoke();
        }
        else
        {
            _headPos += toHook.Normalized() * move;
        }
    }

    private void TickRecoveryPull()
    {
        if (!_recoveryPulling) return;

        // Keep rope head fixed at the hook point while the character flies up toward it.
        _headPos = _recoveryHookPoint;

        float distToHook = (_recoveryHookPoint - _owner.GlobalPosition).Length();
        bool arrived    = distToHook <= RecoveryArrivalThreshold;
        bool fallingBack = _owner.Velocity.Y > 0f; // gravity has reversed them

        if (arrived || fallingBack)
        {
            _recoveryPulling = false;
            _ropeVisible = false;
        }
    }

    // ── Visual ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called every physics frame. Syncs LassoHead position and redraws LassoRope
    /// between the hand anchor and the current lasso head position.
    /// When nothing is active the nodes are hidden.
    /// </summary>
    private void UpdateRopeVisual()
    {
        bool hasNodes = HandAnchor != null && LassoHead != null && LassoRope != null;
        if (!hasNodes) return;

        if (!_ropeVisible)
        {
            LassoHead.Visible = false;
            LassoRope.Visible = false;
            return;
        }

        Vector2 anchor = AnchorPos();

        LassoHead.GlobalPosition = _headPos;
        LassoHead.Visible = true;

        // Sag scales with how much rope is "out" relative to total distance,
        // so it looks slack when close and taut when the rope is fully extended.
        float dist = anchor.DistanceTo(_headPos);
        float sagScale = Mathf.Clamp(dist / LassoRange, 0f, 1f);
        Vector2 midGlobal = (anchor + _headPos) * 0.5f + Vector2.Down * RopeSag * sagScale;

        // Line2D points are in the node's local space, so convert from global.
        LassoRope.SetPointPosition(0, LassoRope.ToLocal(anchor));
        LassoRope.SetPointPosition(1, LassoRope.ToLocal(midGlobal));
        LassoRope.SetPointPosition(2, LassoRope.ToLocal(_headPos));
        LassoRope.Visible = true;
    }

    /// <summary>Returns the hand anchor world position, falling back to the owner position if unset.</summary>
    private Vector2 AnchorPos() =>
        HandAnchor != null ? HandAnchor.GlobalPosition : _owner.GlobalPosition;

    /// <summary>
    /// Resets the lasso head sprite to frame 0 and pauses it.
    /// Called when the lasso is first thrown so it shows the idle/travel frame while in flight.
    /// </summary>
    private void ShowFirstFrame()
    {
        if (LassoHeadSprite == null) return;
        LassoHeadSprite.Stop();
        LassoHeadSprite.Frame = 0;
    }

    /// <summary>
    /// Restarts the hit animation from frame 0 and plays it.
    /// Godot leaves the sprite on the last frame after a one-shot animation finishes,
    /// so Stop() + Frame reset is required before Play() to restart cleanly.
    /// </summary>
    private void PlayHitAnimation()
    {
        if (LassoHeadSprite == null) return;
        LassoHeadSprite.Stop();
        LassoHeadSprite.Frame = 0;
        LassoHeadSprite.Play(LassoHitAnimation);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <param name="collisionMask">
    /// Which physics layers this area detects bodies on.
    /// Use 2 for characters (neutral lasso), 1 for world/floor (down-air lasso).
    /// </param>
    private Area2D CreateArea(float radius, uint collisionMask = 1)
    {
        var area = new Area2D();
        area.CollisionMask = collisionMask;
        var shape = new CollisionShape2D();
        var circle = new CircleShape2D();
        circle.Radius = radius;
        shape.Shape = circle;
        area.AddChild(shape);
        return area;
    }

    private void FreeArea(ref Area2D area)
    {
        if (area != null && IsInstanceValid(area))
        {
            area.QueueFree();
            area = null;
        }
    }
}
