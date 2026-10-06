using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PatienceX;

public readonly record struct DragCardVisual(Border Card, double OffsetY);

public sealed class CardDragAdorner : Adorner
{
    private readonly Canvas _preview;
    private readonly double[] _cardOffsets;
    private readonly TranslateTransform _position = new();

    public CardDragAdorner(
        UIElement adornedElement,
        IReadOnlyList<DragCardVisual> cards,
        double width,
        double height)
        : base(adornedElement)
    {
        if (cards.Count == 0)
            throw new ArgumentException("A drag preview needs at least one card.", nameof(cards));

        IsHitTestVisible = false;
        _preview = new Canvas
        {
            Width = width,
            Height = height,
            RenderTransform = _position
        };
        _cardOffsets = new double[cards.Count];
        for (var index = 0; index < cards.Count; index++)
        {
            var card = cards[index];
            _cardOffsets[index] = card.OffsetY;
            Canvas.SetLeft(card.Card, 0);
            Canvas.SetTop(card.Card, card.OffsetY);
            card.Card.RenderTransform = null;
            _preview.Children.Add(card.Card);
        }
        AddVisualChild(_preview);
        AddLogicalChild(_preview);
    }

    protected override int VisualChildrenCount => 1;
    public int CardCount => _cardOffsets.Length;

    protected override Visual GetVisualChild(int index) =>
        index == 0 ? _preview : throw new ArgumentOutOfRangeException(nameof(index));

    protected override Size ArrangeOverride(Size finalSize)
    {
        _preview.Arrange(new Rect(0, 0, _preview.Width, _preview.Height));
        return finalSize;
    }

    public void SetPosition(Point position)
    {
        var bounds = AdornedElement.RenderSize;
        _position.X = Math.Clamp(position.X, 0, Math.Max(0, bounds.Width - _preview.Width));
        _position.Y = Math.Clamp(position.Y, 0, Math.Max(0, bounds.Height - _preview.Height));
    }

    public void ReleaseCards() => _preview.Children.Clear();

    public void AnimateTo(Point position, IReadOnlyList<double>? destinationOffsets, Action completed)
    {
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(190);
        var xAnimation = new DoubleAnimation(_position.X, position.X, duration)
        {
            EasingFunction = easing
        };
        var yAnimation = new DoubleAnimation(_position.Y, position.Y, duration)
        {
            EasingFunction = easing
        };
        xAnimation.Completed += (_, _) => completed();
        _position.BeginAnimation(TranslateTransform.XProperty, xAnimation);
        _position.BeginAnimation(TranslateTransform.YProperty, yAnimation);

        if (destinationOffsets is null)
            return;
        if (destinationOffsets.Count != _cardOffsets.Length)
            throw new ArgumentException("Each dragged card needs a destination offset.", nameof(destinationOffsets));

        for (var index = 0; index < destinationOffsets.Count; index++)
        {
            var card = (FrameworkElement)_preview.Children[index];
            var transform = new TranslateTransform();
            card.RenderTransform = transform;
            transform.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(
                    0,
                    destinationOffsets[index] - _cardOffsets[index],
                    duration)
                {
                    EasingFunction = easing
                });
        }
    }
}
