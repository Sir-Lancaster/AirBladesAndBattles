using Godot;

public partial class TowerFade : Node
{
    [Export] private Polygon2D _exterior;
    [Export] private float _fadeDuration = 0.4f;

    // Track how many characters are inside — both player and AI can enter.
    // We only restore the exterior once everyone has left.
    private int _charactersInside = 0;
    private Tween _activeTween;

    public void OnBodyEntered(Node2D body)
    {
        if (body is not CharacterBase) return;
        _charactersInside++;
        FadeTo(0f);
    }

    public void OnBodyExited(Node2D body)
    {
        if (body is not CharacterBase) return;
        _charactersInside = Mathf.Max(0, _charactersInside - 1);
        if (_charactersInside == 0)
            FadeTo(1f);
    }

    private void FadeTo(float alpha)
    {
        // Kill any in-progress fade before starting a new one,
        // so entering and exiting quickly doesn't leave the exterior half-faded.
        _activeTween?.Kill();
        _activeTween = CreateTween();
        _activeTween.TweenProperty(_exterior, "modulate:a", alpha, _fadeDuration)
                    .SetTrans(Tween.TransitionType.Sine)
                    .SetEase(Tween.EaseType.InOut);
    }
}