using Godot;
using System;

public partial class Gear : Polygon2D
{
	[Export] public int ProngCount = 12;
	[Export] public float StepPauseSeconds = 0.25f;
	[Export] public bool RotateClockwise = true;

	private float _stepAngleDegrees;

	public override void _Ready()
	{
		_stepAngleDegrees = 360f / ProngCount;
		StartRotationLoop();
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
