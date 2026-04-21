using Godot;
using System.Collections.Generic;

// Draws a gear viewed edge-on (horizontal-axis rotation).
// Barrel body is elliptical (tapers toward edges like a real cylinder side-view).
// Prongs are drawn as outlined rectangles on top of the barrel — centered at y=0,
// growing/shrinking only in Y. Animation matches Gear.cs exactly.
public partial class GearPlatform : Node2D
{
    [Export] public int   ProngCount       = 16;
    [Export] public float StepPauseSeconds  = 0.25f;
    [Export] public float StartDelaySeconds = 0f;
    [Export] public bool  RotateClockwise   = true;

    [Export] public Color FaceColor = new Color(0.708496f,   0.5126037f,  0.08798642f);
    [Export] public Color DarkColor = new Color(0.35050318f, 0.24686277f, 0.02450895f);

    [Export] public float Width       = 351f;
    [Export] public float BodyHeight  = 28f;
    [Export] public float ProngLength = 28f;   
    [Export] public float ProngHalfPx = 15f;

    private float _rotDeg;
    private float _stepAngle;
    private float _hw;
    private const float VisBias = 1.2f;

    public override void _Ready()
    {
        _stepAngle = 360f / ProngCount;
        _hw        = Width / 2f;
        StartRotationLoop();
    }

    private void SetRotAngle(float v) { _rotDeg = v; QueueRedraw(); }

    private async void StartRotationLoop()
    {
        if (StartDelaySeconds > 0f)
            await ToSignal(GetTree().CreateTimer(StartDelaySeconds), SceneTreeTimer.SignalName.Timeout);
        while (IsInsideTree())
        {
            float target = _rotDeg + (RotateClockwise ? _stepAngle : -_stepAngle);
            Tween t = CreateTween();
            t.TweenMethod(Callable.From<float>(SetRotAngle), _rotDeg, target, 0.30)
             .SetTrans(Tween.TransitionType.Sine)
             .SetEase(Tween.EaseType.InOut);
            await ToSignal(t, Tween.SignalName.Finished);
            _rotDeg = target;
            await ToSignal(GetTree().CreateTimer(StepPauseSeconds), SceneTreeTimer.SignalName.Timeout);
        }
    }

    public override void _Draw()
    {
        float bodyHalfH = BodyHeight / 2f;
        float halfProng  = ProngLength / 2f;

        Color tipBright = new(Mathf.Min(FaceColor.R * 1.25f, 1f),
                               Mathf.Min(FaceColor.G * 1.25f, 1f),
                               Mathf.Min(FaceColor.B * 1.25f, 1f));

        Color barrelColor = DarkColor.Lerp(FaceColor, 0.6f);
        DrawRect(new Rect2(-_hw, -bodyHalfH, Width, BodyHeight), barrelColor);
        DrawRect(new Rect2(-_hw, -bodyHalfH * 0.3f, Width, bodyHalfH * 0.6f), FaceColor * 0.6f);

        var prongs = new List<(float cosA, float sinA, float halfH)>(ProngCount);
        for (int i = 0; i < ProngCount; i++)
        {
            float aRad = Mathf.DegToRad(i * _stepAngle + _rotDeg);
            float cosA = Mathf.Cos(aRad);
            float sinA = Mathf.Sin(aRad);
            if (cosA <= 0f) continue; 
            float halfH = halfProng * (cosA + VisBias) / (1f + VisBias);
            prongs.Add((cosA, sinA, halfH));
        }
        prongs.Sort((a, b) => a.cosA.CompareTo(b.cosA)); // far → near

        foreach (var (cosA, sinA, halfH) in prongs)
        {
            float xCen   = sinA * _hw;
            float xLeft  = xCen - ProngHalfPx;
            float prongW = ProngHalfPx * 2f;

            float norm  = (cosA + VisBias) / (1f + VisBias);
            Color fill  = DarkColor.Lerp(FaceColor, norm);
            Color capC  = DarkColor.Lerp(tipBright, norm);
            const float Border = 3f;
            DrawRect(new Rect2(xLeft - Border, -halfH - Border,
                               prongW + Border * 2f, halfH * 2f + Border * 2f), DarkColor);

            DrawRect(new Rect2(xLeft, -halfH, prongW, halfH * 2f), fill);
            DrawRect(new Rect2(xLeft, -halfH,        prongW, 2.5f), capC);
            DrawRect(new Rect2(xLeft,  halfH - 2.5f, prongW, 2.5f), DarkColor);
        }
    }
}
