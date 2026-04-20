using Godot;

public partial class HourHand : ColorRect
{
    [Export] public float SecondsPerTick = 12.0f;

    private float _angle = -Mathf.Pi / 2f;
    private const float StepAngle = Mathf.Tau / 60f;

    public override void _Ready()
    {

        Rotation = _angle;
        Tick();
    }

    private async void Tick()
    {
        while (IsInsideTree())
        {
            await ToSignal(GetTree().CreateTimer(SecondsPerTick), SceneTreeTimer.SignalName.Timeout);
            _angle += StepAngle;
            var tween = CreateTween();
            tween.TweenProperty(this, "rotation", _angle, 0.08f)
                .SetTrans(Tween.TransitionType.Back)
                .SetEase(Tween.EaseType.Out);
            await ToSignal(tween, Tween.SignalName.Finished);
        }
    }
}
