using Godot;

public partial class TowerFade : Node
{
    [Export] private Polygon2D _exterior;
    [Export] private Area2D _trigger;
    [Export] private float _fadeDuration = 0.4f;

    private int _charactersInside = 0;
    private Tween _activeTween;

    public override void _Ready()
    {
        GD.Print($"TowerFade ready. Exterior: {_exterior}, Trigger: {_trigger}");
        _trigger.BodyEntered += OnBodyEntered;
        _trigger.BodyExited += OnBodyExited;
    }


    private void OnBodyEntered(Node2D body)
    {
        GD.Print($"BodyEntered: {body.Name} ({body.GetType().Name})");
        if (body is not CharacterBase) return;
        _charactersInside++;
        FadeTo(0f);
    }

    private void OnBodyExited(Node2D body)
    {
        if (body is not CharacterBase) return;
        _charactersInside = Mathf.Max(0, _charactersInside - 1);
        if (_charactersInside == 0)
            FadeTo(1f);
    }

    private void FadeTo(float alpha)
    {
        _activeTween?.Kill();
        _activeTween = CreateTween();
        _activeTween.TweenProperty(_exterior, "modulate:a", alpha, _fadeDuration)
                    .SetTrans(Tween.TransitionType.Sine)
                    .SetEase(Tween.EaseType.InOut);
    }
}