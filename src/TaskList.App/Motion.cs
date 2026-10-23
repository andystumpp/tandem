using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace TaskList.App;

/// <summary>
/// Small composition animations that answer the user's actions: a spring pop when a task is ticked,
/// rows that slide in when they appear, and fades for expanding, collapsing and theme changes.
/// Everything is skipped when Windows' "Animation effects" setting is off.
/// </summary>
public static class Motion
{
    private static readonly bool Enabled = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;

    private static Visual VisualOf(UIElement element) => ElementCompositionPreview.GetElementVisual(element);

    /// <summary>A quick overshoot from slightly smaller, centered on the element.</summary>
    public static void Pop(FrameworkElement element)
    {
        if (!Enabled) return;
        var visual = VisualOf(element);
        visual.CenterPoint = new Vector3((float)element.ActualWidth / 2, (float)element.ActualHeight / 2, 0);
        var spring = visual.Compositor.CreateSpringVector3Animation();
        spring.InitialValue = new Vector3(0.55f, 0.55f, 1);
        spring.FinalValue = Vector3.One;
        spring.DampingRatio = 0.45f;
        spring.Period = TimeSpan.FromMilliseconds(45);
        visual.StartAnimation("Scale", spring);
    }

    /// <summary>Fades in while sliding up a few pixels, once the element has been laid out.</summary>
    public static void EnterFromBelow(UIElement element, float distance = 10, int delayMs = 0, int durationMs = 320)
    {
        if (!Enabled) return;
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        var visual = VisualOf(element);
        var compositor = visual.Compositor;
        visual.Opacity = 0;
        var ease = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1f), new Vector2(0.3f, 1f));

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(1, 1, ease);
        fade.Duration = TimeSpan.FromMilliseconds(durationMs);
        fade.DelayTime = TimeSpan.FromMilliseconds(delayMs);
        fade.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;

        var slide = compositor.CreateVector3KeyFrameAnimation();
        slide.InsertKeyFrame(0, new Vector3(0, distance, 0));
        slide.InsertKeyFrame(1, Vector3.Zero, ease);
        slide.Duration = fade.Duration;
        slide.DelayTime = fade.DelayTime;
        slide.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;

        visual.StartAnimation("Opacity", fade);
        visual.StartAnimation("Translation", slide);
    }

    /// <summary>Fades out (and drifts up a little), then runs <paramref name="done"/>.</summary>
    public static void Exit(UIElement element, Action done, int durationMs = 170)
    {
        if (!Enabled)
        {
            done();
            return;
        }
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        var visual = VisualOf(element);
        var compositor = visual.Compositor;
        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1, 0);
        fade.Duration = TimeSpan.FromMilliseconds(durationMs);
        var slide = compositor.CreateVector3KeyFrameAnimation();
        slide.InsertKeyFrame(1, new Vector3(0, -6, 0));
        slide.Duration = fade.Duration;
        visual.StartAnimation("Opacity", fade);
        visual.StartAnimation("Translation", slide);

        batch.End();
        batch.Completed += (_, _) => done();
    }

    /// <summary>Fades an element from transparent to fully visible.</summary>
    public static void FadeIn(UIElement element, int durationMs = 450)
    {
        if (!Enabled) return;
        var visual = VisualOf(element);
        var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(1, 1);
        fade.Duration = TimeSpan.FromMilliseconds(durationMs);
        visual.StartAnimation("Opacity", fade);
    }

    /// <summary>Smooth hover fades: background and opacity changes ease instead of snapping.</summary>
    public static void SmoothChanges(Microsoft.UI.Xaml.Controls.Panel panel)
    {
        panel.BackgroundTransition = new Microsoft.UI.Xaml.BrushTransition { Duration = TimeSpan.FromMilliseconds(140) };
    }

    public static void SmoothOpacity(UIElement element)
    {
        element.OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(140) };
    }
}
