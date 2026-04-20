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
/// OTHER GROUPS:
///   quicksand     — the quicksand Polygon2D and its collision shape
///
/// Each Line2D trace must have a ShaderMaterial using TraceChaser.gdshader assigned.
/// The script drives chaser_progress and day_night on those materials every frame.
/// day_night is hardcoded to 1.0 (day) while building — wire it to your overheat
/// cycle timer later.
/// </summary>
public partial class KernelBadlands : Node2D
{
    // ── Exports ───────────────────────────────────────────────────────────────

    /// <summary>Seconds for one full chaser pass across a trace.</summary>
    [Export] public float CycleDuration = 2.0f;

    // DayNight locked to 1.0 while perfecting the daytime look.
    // Restore [Export] public float DayNight when adding the overheat cycle.
    private const float DayNight = 1.0f;

    // ── Trace group config ────────────────────────────────────────────────────

    private static readonly (string Group, float Offset)[] TraceGroups =
    {
        ("kc001_traces",  0.00f),
        ("kc002_traces",  0.33f),
        ("kc003_traces",  0.66f),
        ("ground_traces", 0.15f),
    };

    private static readonly string[] PadGroups =
    {
        "kc001_pads",
        "kc002_pads",
        "kc003_pads",
        "ground_pads",
    };

    // ── Private state ─────────────────────────────────────────────────────────

    private float _chaserProgress;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private int _debugTick;

    public override void _Process(double delta)
    {
        _chaserProgress += (float)delta / CycleDuration;
        if (_chaserProgress >= 1.0f) _chaserProgress -= 1.0f;

        if (++_debugTick % 120 == 0)
        {
            int traceCount = GetTree().GetNodesInGroup("kc001_traces").Count;
            GD.Print($"[KernelBadlands] running — progress={_chaserProgress:F2}  kc001_traces={traceCount}");
        }

        UpdateTraces();
        UpdatePads();
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
                    mat.SetShaderParameter("day_night", DayNight);
                }
            }
        }
    }

    // ── Pad modulation update ─────────────────────────────────────────────────

    private void UpdatePads()
    {
        // Day = full #00FF88 tint, Night = dimmed toward dark.
        float t        = DayNight;
        float r        = Mathf.Lerp(0.0f, 0.0f,  t);
        float g        = Mathf.Lerp(0.1f, 1.0f,  t);
        float b        = Mathf.Lerp(0.05f, 0.533f, t);
        float a        = Mathf.Lerp(0.2f, 1.0f,  t);
        var   padColor = new Color(r, g, b, a);

        foreach (string group in PadGroups)
        {
            foreach (Node node in GetTree().GetNodesInGroup(group))
            {
                if (node is CanvasItem ci)
                    ci.Modulate = padColor;
            }
        }
    }
}