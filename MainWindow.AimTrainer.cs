using System;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using JinxyMac.Core;

namespace JinxyMac;

/// <summary>
/// The Aim Trainer page: click the target, see how fast and how accurate.
/// </summary>
/// <remarks>
/// The one thing the rest of the app cannot do for you. The clicker sends
/// clicks; it does not put your crosshair on anybody. This is practice for that,
/// and it needs no permissions at all — so it works on a Mac where Accessibility
/// is still refusing everything else.
///
/// <para>All the thinking is in <see cref="AimSession"/>, which has no screen
/// and is tested. This draws a circle where it says, reports clicks, and shows
/// the numbers.</para>
/// </remarks>
public partial class MainWindow
{
    private AimSession? _aim;
    private DispatcherTimer? _aimTimer;
    private Stopwatch? _aimClock;
    private AimLevel _aimLevel = AimLevel.Normal;
    private int _aimBest;

    /// <summary>
    /// How often the round is moved on.
    /// </summary>
    /// <remarks>
    /// Fast enough that a target vanishing on time looks immediate, slow enough
    /// to cost nothing: the point of the page is practising aim, not heating up
    /// a laptop.
    /// </remarks>
    private static readonly TimeSpan AimTick = TimeSpan.FromMilliseconds(16);

    private void WireAimTrainer()
    {
        foreach (RadioButton choice in AimLevelPanel.Children.OfType<RadioButton>())
        {
            choice.IsCheckedChanged += (sender, _) =>
            {
                if (sender is not RadioButton { IsChecked: true, Tag: string tag }) return;

                _aimLevel = AimLevel.All.FirstOrDefault(l => l.Name == tag, AimLevel.Normal);

                // Changing difficulty mid-round would make the score meaningless,
                // so it ends the round rather than silently altering it.
                if (_aim?.Running == true) StopAim("Round ended — difficulty changed.");
                else AimHint.Text = $"{_aimLevel.Name}: {_aimLevel.Radius * 2:0}px targets, "
                                    + $"{_aimLevel.LifetimeMs / 1000.0:0.0}s each.";
            };
        }

        AimStartButton.Click += (_, _) =>
        {
            if (_aim?.Running == true) StopAim("Stopped.");
            else StartAim();
        };

        // The click has to be measured where the target is drawn, so the area
        // itself reports it rather than the window.
        AimArea.PointerPressed += (_, e) =>
        {
            if (_aim is not { Running: true } session) return;

            Point at = e.GetPosition(AimArea);
            session.Click(at.X, at.Y, _aimClock?.Elapsed.TotalMilliseconds ?? 0);

            DrawAim();
            ShowAimNumbers();
        };
    }

    private void StartAim()
    {
        double width = AimArea.Bounds.Width, height = AimArea.Bounds.Height;

        if (width < 50 || height < 50)
        {
            AimHint.Text = "Make the window a little bigger to practise in.";
            return;
        }

        _aim = new AimSession(_aimLevel);
        _aim.Start(width, height);

        _aimClock = Stopwatch.StartNew();

        _aimTimer ??= new DispatcherTimer { Interval = AimTick };
        _aimTimer.Tick -= AimTimerTick;
        _aimTimer.Tick += AimTimerTick;
        _aimTimer.Start();

        AimStartButton.Content = "Stop";
        AimHint.Text = "Click the targets.";

        DrawAim();
        ShowAimNumbers();
    }

    private void AimTimerTick(object? sender, EventArgs e)
    {
        if (_aim is not { } session) return;

        if (!session.Tick(_aimClock?.Elapsed.TotalMilliseconds ?? 0))
        {
            StopAim($"Time. {session.Hits} hits at {session.AccuracyPercent:0}%.");
            return;
        }

        DrawAim();
        ShowAimNumbers();
    }

    private void StopAim(string why)
    {
        _aimTimer?.Stop();
        _aimClock?.Stop();

        if (_aim is { } session)
        {
            session.Stop();

            if (session.Score > _aimBest)
            {
                _aimBest = session.Score;
                _settings.AimBest = _aimBest;
                _settings.Save();
            }
        }

        AimStartButton.Content = "Start";
        AimHint.Text = why;

        DrawAim();
        ShowAimNumbers();
    }

    /// <summary>Draws the target, or clears the area between rounds.</summary>
    private void DrawAim()
    {
        AimArea.Children.Clear();

        if (_aim is not { Running: true, Target: { } target }) return;

        // Read from the app's resources rather than hard-coded, so the target is
        // whatever accent the user chose on the Theme page — and because this
        // redraws every frame, changing it mid-round is picked up at once. The
        // app stores colours under "<name>Color"; see Recolour.
        IBrush fill =
            Application.Current?.Resources.TryGetResource("AccentColor", null, out object? accent) == true
            && accent is Color colour
                ? new SolidColorBrush(colour)
                : Brushes.OrangeRed;

        var dot = new Ellipse
        {
            Width = target.Radius * 2,
            Height = target.Radius * 2,
            Fill = fill,
            Stroke = Brushes.White,
            StrokeThickness = 2
        };

        Canvas.SetLeft(dot, target.X - target.Radius);
        Canvas.SetTop(dot, target.Y - target.Radius);

        AimArea.Children.Add(dot);
    }

    private void ShowAimNumbers()
    {
        AimSession? session = _aim;

        AimHitsText.Text = (session?.Hits ?? 0).ToString();
        AimAccuracyText.Text = $"{session?.AccuracyPercent ?? 0:0}%";
        AimReactionText.Text = session is { AverageReactionMs: > 0 }
            ? $"{session.AverageReactionMs:0} ms"
            : "—";
        AimScoreText.Text = (session?.Score ?? 0).ToString();
        AimBestText.Text = _aimBest.ToString();

        if (session is { Running: true })
        {
            double left = Math.Max(0, session.LengthMs - (_aimClock?.Elapsed.TotalMilliseconds ?? 0));
            AimTimeText.Text = $"{left / 1000.0:0.0}s";
        }
        else
        {
            AimTimeText.Text = $"{(session?.LengthMs ?? 30_000) / 1000.0:0}s";
        }
    }

    /// <summary>Puts the saved best score back, and the level labels in place.</summary>
    private void LoadAimTrainer(AppSettings s)
    {
        _aimBest = Math.Max(0, s.AimBest);
        ShowAimNumbers();
    }

    /// <summary>Stops a round when the page is left, so it cannot tick unseen.</summary>
    private void LeavingAimTrainer()
    {
        if (_aim?.Running == true) StopAim("Stopped — you left the page.");
    }
}
