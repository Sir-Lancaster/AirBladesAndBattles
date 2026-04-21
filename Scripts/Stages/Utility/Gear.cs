using Godot;
using System;

public partial class Gear : Polygon2D
{
    [Export] public int ProngCount = 12;
    [Export] public float StepPauseSeconds = 0.25f;
    [Export] public bool RotateClockwise = true;
    [Export] public float HoleRadius = 100f;
    [Export] public Color HoleColor = Color.FromHtml("#66461d");
    [Export] public bool ShowHub = true;
    [Export] public Material HoleMaterial;


    private float _stepAngleDegrees;

    public override void _Ready()
    {
        if (ShowHub) AddHoleOverlay();
        _stepAngleDegrees = 360f / ProngCount;
        StartRotationLoop();
    }

    private void AddHoleOverlay()
    {
        // Centroid + offset gives the gear's visual center in local node space
        var center = Vector2.Zero;
        foreach (var v in Polygon) center += v;
        center = center / Polygon.Length + Offset;

        const int segments = 24;
        var circle = new Vector2[segments];
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.Tau / segments;
            circle[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * HoleRadius;
        }

        var hub = new Polygon2D();
        hub.Position = center;
        hub.Polygon = circle;
        hub.Color = HoleColor;
        if (HoleMaterial != null) hub.Material = HoleMaterial;
        AddChild(hub);
    }

    private async void StartRotationLoop()
    {
        while (IsInsideTree())
        {
            float target = RotationDegrees + (RotateClockwise ? _stepAngleDegrees : -_stepAngleDegrees);

            Tween tween = CreateTween();
            tween.TweenProperty(this, "rotation_degrees", target, 0.30f)
                .SetTrans(Tween.TransitionType.Sine)
                .SetEase(Tween.EaseType.InOut);

            await ToSignal(tween, Tween.SignalName.Finished);
            await ToSignal(GetTree().CreateTimer(StepPauseSeconds), SceneTreeTimer.SignalName.Timeout);
        }
    }
}
