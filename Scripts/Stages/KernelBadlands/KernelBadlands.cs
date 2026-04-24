using Godot;

/// <summary>
/// Attach to the root Node2D of KernelBadlands.tscn.
///
/// TRACE GROUPS (add every Line2D trace node to these in the editor):
///   kc001_traces  — chipLine# nodes inside KC-001's Traces Node2D  (offset 0.00)
///   kc002_traces  — chipLine# nodes inside KC-002's Traces Node2D  (offset 0.33)
///   kc003_traces  — chipLine# nodes inside KC-003's Traces Node2D  (offset 0.66)
///   ground_traces — chipLine# nodes on the ground plane            (offset 0.15)
///
/// PAD GROUPS (add every SolderPad Polygon2D to these in the editor):
///   kc001_pads, kc002_pads, kc003_pads, ground_pads
///
/// LABEL GROUPS (add each mesa label node to these in the editor):
///   kc001_labels, kc002_labels, kc003_labels
///
/// OTHER GROUPS:
///   quicksand — the quicksand Polygon2D and its collision shape
///
/// Each Line2D trace must have a ShaderMaterial using TraceChaser.gdshader assigned.
/// Drag the sky and sun Polygon2Ds into their exports so day_night drives their shaders too.
/// </summary>
public partial class KernelBadlands : Node2D
{
    // ── Exports ───────────────────────────────────────────────────────────────

    /// <summary>Seconds for one full chaser pass across a trace.</summary>
    [Export] public float CycleDuration = 2.0f;

    /// <summary>How long each phase (day or dusk) holds before the next transition begins.</summary>
    [Export] public float PhaseDuration = 10.0f;
    /// <summary>Units per second that day_night moves during a transition (1.0 = 1 s to cross full range).</summary>
    [Export] public float TransitionSpeed = 0.4f;

    // Drag each mesa Node2D into these in the editor.
    [Export] private Node2D _mesaKC001;
    [Export] private Node2D _mesaKC002;
    [Export] private Node2D _mesaKC003;

    /// <summary>Node2D parent containing the ground base polygon and topsoil polygon.</summary>
    [Export] private Node2D _ground;
    /// <summary>TopEdge Line2D — slightly lighter dusk tint so the platform edge stays readable.</summary>
    [Export] private Node2D _groundTopEdge;

    /// <summary>ColorRect whose ShaderMaterial uses DaySky.gdshader.</summary>
    [Export] private ColorRect _skyRect;
    /// <summary>ColorRect whose ShaderMaterial uses DaySun.gdshader.</summary>
    [Export] private ColorRect _sunRect;
    /// <summary>How many pixels the sun drops from its day position at full dusk.</summary>
    [Export] public float SunDuskDropY = 100f;
    /// <summary>ColorRect whose ShaderMaterial uses Stars.gdshader.</summary>
    [Export] private ColorRect _starsRect;

    /// <summary>Node2D holding the quicksand visual polygons.</summary>
    [Export] private Node2D _quicksandVisuals;
    /// <summary>CollisionShape2D for the quicksand platform — disabled while quicksand is active.</summary>
    [Export] private CollisionShape2D _quicksandCollision;
    /// <summary>Seconds between quicksand events (only triggers during daytime).</summary>
    [Export] public float QuicksandInterval = 25f;
    /// <summary>How long the quicksand stays open (collision off, visuals on).</summary>
    [Export] public float QuicksandDuration = 10f;

    // ── Trace group config ────────────────────────────────────────────────────

    private static readonly (string Group, float Offset)[] TraceGroups =
    {
        ("kc001_traces",  0.00f),
        ("kc002_traces",  0.33f),
        ("kc003_traces",  0.66f),
        ("ground_traces", 0.15f),
    };

    private static readonly string[] LabelGroups =
    {
        "kc001_labels",
        "kc002_labels",
        "kc003_labels",
    };

    // ── Private state ─────────────────────────────────────────────────────────

    private float _chaserProgress;

    /// <summary>1.0 = full day, 0.0 = full dusk.</summary>
    private float _dayNight       = 1.0f;
    private float _dayNightTarget = 1.0f;
    private float _phaseTimer     = 0.0f;

    private float _sunOriginY;

    private float _quicksandIntervalTimer  = 0f;
    private float _quicksandActiveTimer    = 0f;
    private bool  _quicksandOpen           = false;
    private bool  _quicksandFiredThisDay   = false;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        if (_sunRect != null) _sunOriginY = _sunRect.Position.Y;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        _chaserProgress += dt / CycleDuration;
        if (_chaserProgress >= 1.0f) _chaserProgress -= 1.0f;

        TickDayNight(dt);

        UpdateTraces();
        UpdatePads();
        UpdateLabels();
        UpdateMesas();
        UpdateGround();
        UpdateSkyShaders();
        TickQuicksand(dt);
    }

    // ── Day/dusk cycle ────────────────────────────────────────────────────────

    private void TickDayNight(float dt)
    {
        _phaseTimer += dt;
        if (_phaseTimer >= PhaseDuration)
        {
            _phaseTimer     -= PhaseDuration;
            _dayNightTarget  = _dayNightTarget > 0.5f ? 0.0f : 1.0f;
            // New day starting — allow quicksand to fire once again.
            if (_dayNightTarget > 0.5f) _quicksandFiredThisDay = false;
        }

        _dayNight = Mathf.MoveToward(_dayNight, _dayNightTarget, TransitionSpeed * dt);
    }

    // ── Sky / sun shader update ───────────────────────────────────────────────

    private void UpdateSkyShaders()
    {
        if (_skyRect?.Material is ShaderMaterial skyMat)
            skyMat.SetShaderParameter("day_night", _dayNight);

        if (_starsRect?.Material is ShaderMaterial starsMat)
            starsMat.SetShaderParameter("day_night", _dayNight);

        if (_sunRect != null)
        {
            if (_sunRect.Material is ShaderMaterial sunMat)
                sunMat.SetShaderParameter("day_night", _dayNight);

            float targetY = _sunOriginY + Mathf.Lerp(SunDuskDropY, 0f, _dayNight);
            _sunRect.Position = new Vector2(_sunRect.Position.X, targetY);
        }
    }

    // ── Trace shader update ───────────────────────────────────────────────────

    private void UpdateTraces()
    {
        foreach (var (group, offset) in TraceGroups)
        {
            float groupProgress = (_chaserProgress + offset) % 1.0f;

            foreach (Node node in GetTree().GetNodesInGroup(group))
            {
                if (node is Line2D line && line.Material is ShaderMaterial mat)
                {
                    mat.SetShaderParameter("chaser_progress", groupProgress);
                    mat.SetShaderParameter("day_night", _dayNight);
                }
            }
        }
    }

    // ── Ground modulation update ──────────────────────────────────────────────

    private static readonly Color GroundDay   = new(1f, 1f, 1f, 1f);
    private static readonly Color GroundDusk  = new(0.30f, 0.15f, 0.06f, 1f); // #4D2610
    private static readonly Color TopEdgeDusk = new(0.42f, 0.22f, 0.09f, 1f); // #6B3817

    private void UpdateGround()
    {
        float t = 1.0f - _dayNight;
        if (_ground        != null) _ground.Modulate        = GroundDay.Lerp(GroundDusk,  t);
        if (_groundTopEdge != null) _groundTopEdge.Modulate = GroundDay.Lerp(TopEdgeDusk, t);
    }

    // ── Mesa silhouette update ────────────────────────────────────────────────

    private static readonly Color MesaDay  = new(1f, 1f, 1f, 1f);
    private static readonly Color MesaDusk = new(0.102f, 0.031f, 0.016f, 1f); // #1A0804

    private void UpdateMesas()
    {
        var tint = MesaDay.Lerp(MesaDusk, 1.0f - _dayNight);
        if (_mesaKC001 != null) _mesaKC001.Modulate = tint;
        if (_mesaKC002 != null) _mesaKC002.Modulate = tint;
        if (_mesaKC003 != null) _mesaKC003.Modulate = tint;
    }

    // ── Quicksand ─────────────────────────────────────────────────────────────

    private void TickQuicksand(float dt)
    {
        if (_quicksandOpen)
        {
            _quicksandActiveTimer += dt;
            if (_quicksandActiveTimer >= QuicksandDuration)
            {
                _quicksandOpen = false;
                _quicksandIntervalTimer = 0f;
                if (_quicksandCollision != null) _quicksandCollision.Disabled = false;
                if (_quicksandVisuals   != null) _quicksandVisuals.Visible    = false;
                GD.Print("[KernelBadlands] Quicksand closed.");
            }
        }
        else
        {
            // Only tick the interval during daytime so it never triggers at dusk.
            if (_dayNight > 0.8f)
                _quicksandIntervalTimer += dt;

            if (_quicksandIntervalTimer >= QuicksandInterval && !_quicksandFiredThisDay)
            {
                _quicksandOpen           = true;
                _quicksandFiredThisDay   = true;
                _quicksandActiveTimer    = 0f;
                if (_quicksandCollision != null) _quicksandCollision.Disabled = true;
                if (_quicksandVisuals   != null) _quicksandVisuals.Visible    = true;
                GD.Print("[KernelBadlands] Quicksand opened — collision disabled.");
            }
        }
    }

    // ── Pad shader update ─────────────────────────────────────────────────────
    // Requires a ShaderMaterial using NightGlow.gdshader assigned to each pad node.

    // The parent mesa/ground nodes are modulated dark at night which cascades to all
    // children including pads and labels. The inverse modulate counteracts that so the
    // NightGlow shader output reaches the screen at its correct brightness/colour.

    private Color InverseOf(Color c) => new(
        1f / Mathf.Max(c.R, 0.001f),
        1f / Mathf.Max(c.G, 0.001f),
        1f / Mathf.Max(c.B, 0.001f),
        1f
    );

    private static readonly string[] MesaPadGroups   = { "kc001_pads", "kc002_pads", "kc003_pads" };
    private static readonly string[] GroundPadGroups  = { "ground_pads" };

    private void UpdatePads()
    {
        float t           = 1f - _dayNight;
        Color mesaComp    = InverseOf(MesaDay.Lerp(MesaDusk,   t));
        Color groundComp  = InverseOf(GroundDay.Lerp(GroundDusk, t));

        foreach (string group in MesaPadGroups)
            ApplyNightGlow(group, mesaComp);

        foreach (string group in GroundPadGroups)
            ApplyNightGlow(group, groundComp);
    }

    // ── Label shader update ───────────────────────────────────────────────────

    private void UpdateLabels()
    {
        float t        = 1f - _dayNight;
        Color mesaComp = InverseOf(MesaDay.Lerp(MesaDusk, t));

        foreach (string group in LabelGroups)
            ApplyNightGlow(group, mesaComp);
    }

    private void ApplyNightGlow(string group, Color compensation)
    {
        foreach (Node node in GetTree().GetNodesInGroup(group))
        {
            if (node is not CanvasItem ci) continue;
            ci.Modulate = compensation;
            if (ci.Material is ShaderMaterial mat)
                mat.SetShaderParameter("day_night", _dayNight);
        }
    }
}
