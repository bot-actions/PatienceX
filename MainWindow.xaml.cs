using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace PatienceX;

public partial class MainWindow : Window
{
    private const double CardAspectRatio = 1.42;
    private const double CardWidthLimit = 108;
    private readonly GameEngine _game = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Stopwatch _clock = new();
    private CardSource? _selected;
    private int _selectedFoundation = -1;
    private CardDragAdorner? _dragAdorner;
    private Vector _dragCursorOffset;
    private bool _dropAnimationPending;
    private bool _isDropAnimating;
    private bool _hasStarted;

    public MainWindow()
    {
        InitializeComponent();
        _game.NewGame();
        _timer.Tick += (_, _) => UpdateStats();
        _timer.Start();
        BoardCanvas.Loaded += (_, _) => RenderBoard();
        UpdateStats();
        UpdateModeMenu();
    }

    private void RenderBoard()
    {
        if (BoardCanvas.ActualWidth < 1 || BoardCanvas.ActualHeight < 1)
            return;

        BoardCanvas.Children.Clear();
        var margin = 22d;
        var gap = 12d;
        var availableColumnWidth = (BoardCanvas.ActualWidth - margin * 2 - gap * 6) / 7;
        var cardWidth = Math.Min(CardWidthLimit, availableColumnWidth);
        margin = (BoardCanvas.ActualWidth - (cardWidth * 7 + gap * 6)) / 2;
        var cardHeight = cardWidth * CardAspectRatio;
        var stepX = cardWidth + gap;
        var topY = 26d;
        var tableauY = topY + cardHeight + 37d;
        var hiddenOffset = 13d;
        var foundationStart = margin + 3 * stepX;

        DrawSlot(margin, topY, cardWidth, cardHeight, "STOCK", OnStockClick);
        DrawStockBacks(margin, topY, cardWidth, cardHeight);
        DrawSlot(margin + stepX, topY, cardWidth, cardHeight, "WASTE", null);
        DrawWaste(margin + stepX, topY, cardWidth, cardHeight);
        for (var i = 0; i < _game.Foundations.Length; i++)
        {
            var x = foundationStart + i * stepX;
            var foundationIndex = i;
            DrawSlot(x, topY, cardWidth, cardHeight, new[] { "♣", "♦", "♥", "♠" }[foundationIndex],
                () => OnFoundationClick(foundationIndex), PileKind.Foundation, foundationIndex);
            if (_game.Foundations[i].Count > 0)
                DrawCard(_game.Foundations[i][^1], x, topY, cardWidth, cardHeight,
                    new CardSource(PileKind.Foundation, i, _game.Foundations[i].Count - 1));
        }

        for (var column = 0; column < _game.Tableau.Length; column++)
        {
            var columnIndex = column;
            var x = margin + column * stepX;
            DrawSlot(x, tableauY, cardWidth, cardHeight, "KING", () => OnTableauSlotClick(columnIndex),
                PileKind.Tableau, columnIndex);
            var y = tableauY;
            var visibleOffset = GetTableauVisibleOffset(column, tableauY, cardHeight, hiddenOffset);
            for (var row = 0; row < _game.Tableau[column].Count; row++)
            {
                var card = _game.Tableau[column][row];
                DrawCard(card, x, y, cardWidth, cardHeight,
                    new CardSource(PileKind.Tableau, column, row));
                y += card.FaceUp ? visibleOffset : hiddenOffset;
            }
        }

        DrawLabels(margin, topY + cardHeight + 7, stepX, foundationStart);
        WinBanner.Visibility = _game.IsWon ? Visibility.Visible : Visibility.Collapsed;
    }

    private void DrawSlot(
        double x,
        double y,
        double width,
        double height,
        string label,
        Action? action,
        PileKind? dropKind = null,
        int dropIndex = 0)
    {
        var slot = new Border
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(9),
            Background = new SolidColorBrush(Color.FromArgb(18, 240, 255, 246)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(68, 218, 245, 226)),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Color.FromArgb(88, 224, 246, 233)),
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            },
            Cursor = action is null ? Cursors.Arrow : Cursors.Hand
        };
        slot.Tag = dropKind is null ? null : (dropKind.Value, dropIndex);
        if (action is not null)
            slot.MouseLeftButtonUp += (_, e) => { action(); e.Handled = true; };
        if (dropKind is { } kind)
            EnableDropTarget(slot, kind, dropIndex, isEmptySlot: true);
        Canvas.SetLeft(slot, x);
        Canvas.SetTop(slot, y);
        BoardCanvas.Children.Add(slot);
    }

    private void DrawStockBacks(double x, double y, double width, double height)
    {
        if (_game.Stock.Count == 0)
            return;

        var visibleCount = Math.Min(3, _game.Stock.Count);
        for (var i = 0; i < visibleCount; i++)
        {
            var back = new Border
            {
                Width = width,
                Height = height,
                CornerRadius = new CornerRadius(9),
                Background = new LinearGradientBrush(Color.FromRgb(58, 113, 89), Color.FromRgb(28, 76, 59), 35),
                BorderBrush = new SolidColorBrush(Color.FromRgb(178, 218, 191)),
                BorderThickness = new Thickness(1),
                Child = CreateCardBackContent(width, height),
                Cursor = Cursors.Hand
            };
            back.Tag = "StockBack";
            back.MouseLeftButtonUp += (_, e) => { OnStockClick(); e.Handled = true; };
            Canvas.SetLeft(back, x + i * 3);
            Canvas.SetTop(back, y);
            BoardCanvas.Children.Add(back);
        }
    }

    private static UIElement CreateCardBackContent(double width, double height)
    {
        var inner = new Border
        {
            Margin = new Thickness(5),
            CornerRadius = new CornerRadius(6),
            BorderBrush = new SolidColorBrush(Color.FromArgb(130, 215, 241, 222)),
            BorderThickness = new Thickness(1),
            Background = new DrawingBrush
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, 14, 14),
                ViewportUnits = BrushMappingMode.Absolute,
                Drawing = new GeometryDrawing(
                    new SolidColorBrush(Color.FromRgb(56, 113, 87)),
                    new Pen(new SolidColorBrush(Color.FromArgb(55, 219, 246, 226)), 1),
                    new GeometryGroup
                    {
                        Children = new GeometryCollection
                        {
                            new LineGeometry(new Point(0, 0), new Point(14, 14)),
                            new LineGeometry(new Point(14, 0), new Point(0, 14))
                        }
                    })
            }
        };
        var grid = new Grid();
        grid.Children.Add(inner);
        grid.Children.Add(new TextBlock
        {
            Text = "PX",
            FontSize = Math.Max(11, width * 0.15),
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromArgb(210, 233, 249, 239)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        });
        return grid;
    }

    private void DrawWaste(double x, double y, double width, double height)
    {
        var shown = _game.DrawCount == 3
            ? _game.Waste.Skip(Math.Max(0, _game.Waste.Count - 3)).ToList()
            : _game.Waste.TakeLast(1).ToList();
        for (var i = 0; i < shown.Count; i++)
        {
            var cardIndex = _game.Waste.Count - shown.Count + i;
            DrawCard(shown[i], x + i * Math.Min(20, width * 0.24), y, width, height,
                new CardSource(PileKind.Waste, 0, cardIndex));
        }
    }

    private void DrawCard(Card card, double x, double y, double width, double height, CardSource source)
    {
        var isSelected = _selected == source ||
                         source.Kind == PileKind.Tableau &&
                         _selected is { Kind: PileKind.Tableau } selected &&
                         selected.PileIndex == source.PileIndex && source.CardIndex >= selected.CardIndex ||
                         source.Kind == PileKind.Foundation && _selectedFoundation == source.PileIndex;
        Border cardBorder;
        if (!card.FaceUp)
        {
            cardBorder = new Border
            {
                Width = width,
                Height = height,
                CornerRadius = new CornerRadius(9),
                Background = new LinearGradientBrush(Color.FromRgb(58, 113, 89), Color.FromRgb(28, 76, 59), 35),
                BorderBrush = new SolidColorBrush(Color.FromRgb(178, 218, 191)),
                BorderThickness = new Thickness(isSelected ? 3 : 1),
                Child = CreateCardBackContent(width, height),
                Cursor = Cursors.Hand
            };
            cardBorder.Tag = "StockBack";
        }
        else
        {
            var ink = card.IsRed ? Color.FromRgb(190, 57, 65) : Color.FromRgb(31, 45, 41);
            var grid = new Grid { Margin = new Thickness(8, 7, 8, 7) };
            var corner = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Left };
            corner.Children.Add(new TextBlock
            {
                Text = card.RankLabel,
                FontSize = Math.Max(14, width * 0.18),
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(ink),
                LineHeight = width * 0.2
            });
            corner.Children.Add(new TextBlock
            {
                Text = card.SuitSymbol,
                FontSize = Math.Max(11, width * 0.13),
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(ink),
                Margin = new Thickness(0, -2, 0, 0)
            });
            grid.Children.Add(corner);
            grid.Children.Add(new TextBlock
            {
                Text = card.SuitSymbol,
                FontSize = width * 0.43,
                Foreground = new SolidColorBrush(Color.FromArgb(205, ink.R, ink.G, ink.B)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            });
            var lowerCorner = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                RenderTransform = new RotateTransform(180),
                RenderTransformOrigin = new Point(0.5, 0.5),
                IsHitTestVisible = false
            };
            lowerCorner.Children.Add(new TextBlock
            {
                Text = card.RankLabel,
                FontSize = Math.Max(14, width * 0.18),
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(ink),
                LineHeight = width * 0.2
            });
            lowerCorner.Children.Add(new TextBlock
            {
                Text = card.SuitSymbol,
                FontSize = Math.Max(11, width * 0.13),
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(ink),
                Margin = new Thickness(0, -2, 0, 0)
            });
            grid.Children.Add(lowerCorner);

            cardBorder = new Border
            {
                Width = width,
                Height = height,
                CornerRadius = new CornerRadius(9),
                Background = new LinearGradientBrush(Color.FromRgb(255, 255, 252), Color.FromRgb(241, 243, 235), 90),
                BorderBrush = isSelected ? new SolidColorBrush(Color.FromRgb(112, 232, 169)) :
                    new SolidColorBrush(Color.FromRgb(203, 214, 202)),
                BorderThickness = new Thickness(isSelected ? 3 : 1),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 9,
                    ShadowDepth = 2,
                    Opacity = 0.2
                },
                Child = grid,
                Cursor = Cursors.Hand
            };
        }

        cardBorder.Tag = source;
        var dragStarted = false;
        Point dragStart = default;
        cardBorder.MouseLeftButtonDown += (_, e) => dragStart = e.GetPosition(cardBorder);
        cardBorder.MouseMove += (_, e) =>
        {
            if (_isDropAnimating || e.LeftButton != MouseButtonState.Pressed || dragStarted ||
                !_game.IsMovableSource(source))
                return;

            var current = e.GetPosition(cardBorder);
            if (Math.Abs(current.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(current.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            dragStarted = true;
            _selected = null;
            _selectedFoundation = -1;
            var startPosition = cardBorder.TranslatePoint(new Point(0, 0), BoardCanvas);
            _dragCursorOffset = e.GetPosition(BoardCanvas) - startPosition;
            var adornerLayer = AdornerLayer.GetAdornerLayer(BoardCanvas);
            if (adornerLayer is null)
            {
                dragStarted = false;
                StatusText.Text = "Card dragging is unavailable. Select a card and click its destination instead.";
                return;
            }

            var dragCards = GetDragCardVisuals(source, cardBorder);
            var previewHeight = dragCards.Max(card => card.OffsetY + card.Card.ActualHeight);
            foreach (var dragCard in dragCards)
                BoardCanvas.Children.Remove(dragCard.Card);

            _dragAdorner = new CardDragAdorner(
                BoardCanvas, dragCards, cardBorder.ActualWidth, previewHeight);
            adornerLayer.Add(_dragAdorner);
            _dragAdorner.SetPosition(startPosition);
            try
            {
                DragDrop.DoDragDrop(cardBorder, new DataObject(typeof(CardSource), source), DragDropEffects.Move);
            }
            finally
            {
                if (!_dropAnimationPending)
                {
                    RemoveDragAdorner();
                    RenderBoard();
                }
                dragStarted = false;
            }
        };
        cardBorder.MouseLeftButtonUp += (_, e) =>
        {
            if (!dragStarted)
                OnCardClick(source, e.ClickCount);
            e.Handled = true;
        };
        if (source.Kind is PileKind.Tableau or PileKind.Foundation)
            EnableDropTarget(cardBorder, source.Kind, source.PileIndex, isEmptySlot: false);
        Canvas.SetLeft(cardBorder, x);
        Canvas.SetTop(cardBorder, y);
        BoardCanvas.Children.Add(cardBorder);
    }

    private void EnableDropTarget(
        UIElement element,
        PileKind destination,
        int destinationIndex,
        bool isEmptySlot)
    {
        element.AllowDrop = true;
        element.DragOver += (_, e) =>
        {
            UpdateDragAdornerPosition(e);
            e.Effects = e.Data.GetDataPresent(typeof(CardSource))
                ? DragDropEffects.Move
                : DragDropEffects.None;
            e.Handled = true;
        };
        element.Drop += (_, e) =>
        {
            if (e.Data.GetData(typeof(CardSource)) is CardSource source)
                BeginDropAnimation(source, destination, destinationIndex, element, isEmptySlot);
            e.Handled = true;
        };
    }

    private List<DragCardVisual> GetDragCardVisuals(CardSource source, Border firstCard)
    {
        if (source.Kind != PileKind.Tableau)
            return [new DragCardVisual(firstCard, 0)];

        var firstCardY = Canvas.GetTop(firstCard);
        return BoardCanvas.Children
            .OfType<Border>()
            .Where(card => card.Tag is CardSource cardSource &&
                           cardSource.Kind == PileKind.Tableau &&
                           cardSource.PileIndex == source.PileIndex &&
                           cardSource.CardIndex >= source.CardIndex)
            .OrderBy(card => ((CardSource)card.Tag).CardIndex)
            .Select(card => new DragCardVisual(card, Canvas.GetTop(card) - firstCardY))
            .ToList();
    }

    private void BeginDropAnimation(
        CardSource source,
        PileKind destination,
        int destinationIndex,
        UIElement target,
        bool isEmptySlot)
    {
        if (_isDropAnimating || _dragAdorner is null)
            return;

        var targetPosition = target.TranslatePoint(new Point(0, 0), BoardCanvas);
        IReadOnlyList<double>? destinationOffsets = null;
        if (destination == PileKind.Tableau && !isEmptySlot && target is FrameworkElement card)
        {
            var tableauY = 26 + card.ActualHeight + 37;
            var visibleOffset = GetTableauVisibleOffset(
                destinationIndex, tableauY, card.ActualHeight, 13, GetCardCount(source));
            targetPosition.Y += visibleOffset;
            destinationOffsets = Enumerable.Range(0, _dragAdorner.CardCount)
                .Select(index => index * visibleOffset)
                .ToArray();
        }
        else if (destination == PileKind.Tableau && isEmptySlot && target is FrameworkElement emptySlot)
        {
            var tableauY = 26 + emptySlot.ActualHeight + 37;
            var visibleOffset = GetTableauVisibleOffset(
                destinationIndex, tableauY, emptySlot.ActualHeight, 13, _dragAdorner.CardCount);
            destinationOffsets = Enumerable.Range(0, _dragAdorner.CardCount)
                .Select(index => index * visibleOffset)
                .ToArray();
        }

        _dropAnimationPending = true;
        _isDropAnimating = true;
        _dragAdorner.AnimateTo(targetPosition, destinationOffsets, () =>
        {
            RemoveDragAdorner();
            _dropAnimationPending = false;
            var moved = destination switch
            {
                PileKind.Tableau => _game.TryMoveToTableau(source, destinationIndex),
                PileKind.Foundation => _game.TryMoveToFoundation(source, destinationIndex),
                _ => false
            };

            _selected = null;
            _selectedFoundation = -1;
            StatusText.Text = moved
                ? destination == PileKind.Foundation ? "Card moved to foundation." : "Nice move."
                : "That move is not allowed. Build down in alternating colors or up by suit in foundations.";
            _isDropAnimating = false;
            CompleteCardAction(moved);
        });
    }

    private void RemoveDragAdorner()
    {
        if (_dragAdorner is null)
            return;

        var layer = AdornerLayer.GetAdornerLayer(BoardCanvas);
        _dragAdorner.ReleaseCards();
        if (layer is not null)
            layer.Remove(_dragAdorner);
        _dragAdorner = null;
    }

    private void BoardCanvas_DragOver(object sender, DragEventArgs e)
    {
        UpdateDragAdornerPosition(e);
        e.Effects = e.Data.GetDataPresent(typeof(CardSource))
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void UpdateDragAdornerPosition(DragEventArgs e)
    {
        if (_dragAdorner is null || _dropAnimationPending)
            return;

        _dragAdorner.SetPosition(e.GetPosition(BoardCanvas) - _dragCursorOffset);
    }

    private double GetTableauVisibleOffset(
        int column,
        double tableauY,
        double cardHeight,
        double hiddenOffset,
        int additionalFaceUpCards = 0)
    {
        var pile = _game.Tableau[column];
        var firstFaceUp = pile.FindIndex(card => card.FaceUp);
        var hiddenCount = firstFaceUp < 0 ? pile.Count : firstFaceUp;
        var faceUpCount = pile.Count - hiddenCount + additionalFaceUpCards;
        if (faceUpCount <= 1)
            return 34;

        var availableHeight = BoardCanvas.ActualHeight - 12 - tableauY - cardHeight -
                              hiddenCount * hiddenOffset - 8;
        var fitSpacing = availableHeight / (faceUpCount - 1);
        return Math.Max(1, Math.Min(34, fitSpacing));
    }

    private int GetCardCount(CardSource source)
    {
        var pile = source.Kind switch
        {
            PileKind.Waste => _game.Waste,
            PileKind.Tableau when source.PileIndex >= 0 && source.PileIndex < _game.Tableau.Length =>
                _game.Tableau[source.PileIndex],
            PileKind.Foundation when source.PileIndex >= 0 && source.PileIndex < _game.Foundations.Length =>
                _game.Foundations[source.PileIndex],
            _ => []
        };
        return Math.Max(0, pile.Count - source.CardIndex);
    }

    private void DrawLabels(double margin, double y, double stepX, double foundationStart)
    {
        AddLabel(margin, y, stepX, "STOCK");
        AddLabel(margin + stepX, y, stepX, "WASTE");
        for (var i = 0; i < 4; i++)
            AddLabel(foundationStart + i * stepX, y, stepX, "FOUNDATION");
    }

    private void AddLabel(double x, double y, double width, string text)
    {
        var label = new TextBlock
        {
            Text = text,
            Width = width,
            TextAlignment = TextAlignment.Center,
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(150, 210, 235, 221)),
            IsHitTestVisible = false
        };
        Canvas.SetLeft(label, x);
        Canvas.SetTop(label, y);
        BoardCanvas.Children.Add(label);
    }

    private void OnCardClick(CardSource source, int clickCount)
    {
        if (clickCount > 1 && _game.TryAutoMoveToFoundation(source))
        {
            _selected = null;
            _selectedFoundation = -1;
            _hasStarted = true;
            _clock.Start();
            StatusText.Text = "Card moved to foundation.";
            RenderBoard();
            UpdateStats();
            CheckForWin();
            return;
        }

        if (_selected is { } selected)
        {
            if (selected == source)
            {
                _selected = null;
                StatusText.Text = "Selection cleared.";
            }
            else if (source.Kind == PileKind.Tableau)
            {
                var moved = _game.TryMoveToTableau(selected, source.PileIndex);
                _selected = null;
                StatusText.Text = moved ? "Nice move." : "That move is not allowed. Build down in alternating colors.";
                CompleteCardAction(moved);
                return;
            }
            else if (source.Kind == PileKind.Foundation)
            {
                var moved = _game.TryMoveToFoundation(selected, source.PileIndex);
                _selected = null;
                StatusText.Text = moved ? "Card moved to foundation." : "Foundations build up by suit, starting with an Ace.";
                CompleteCardAction(moved);
                return;
            }
            else if (_game.IsMovableSource(source))
            {
                _selected = source;
                StatusText.Text = "Card selected. Choose a tableau column or a foundation.";
            }
            else
            {
                StatusText.Text = "That card cannot be moved right now.";
            }
            RenderBoard();
            return;
        }

        if (_selectedFoundation >= 0)
        {
            if (source.Kind == PileKind.Tableau)
            {
                var moved = _game.TryMoveToTableau(
                    new CardSource(PileKind.Foundation, _selectedFoundation,
                        _game.Foundations[_selectedFoundation].Count - 1),
                    source.PileIndex);
                _selectedFoundation = -1;
                StatusText.Text = moved ? "Foundation card moved to the tableau." : "That move is not allowed.";
                CompleteCardAction(moved);
                return;
            }
            _selectedFoundation = -1;
        }

        if (_game.IsMovableSource(source))
        {
            _selected = source;
            StatusText.Text = "Card selected. Choose a tableau column or a foundation.";
        }
        else
        {
            StatusText.Text = "That card cannot be moved right now.";
        }
        RenderBoard();
    }

    private void OnTableauSlotClick(int column)
    {
        if (_selected is { } selected)
        {
            var moved = _game.TryMoveToTableau(selected, column);
            _selected = null;
            StatusText.Text = moved ? "Nice move." : "Only a King can be placed in an empty column.";
            CompleteCardAction(moved);
        }
        else if (_selectedFoundation >= 0)
        {
            var moved = _game.TryMoveToTableau(
                new CardSource(PileKind.Foundation, _selectedFoundation,
                    _game.Foundations[_selectedFoundation].Count - 1),
                column);
            _selectedFoundation = -1;
            StatusText.Text = moved ? "Foundation card moved to the tableau." : "Only a King can be placed in an empty column.";
            CompleteCardAction(moved);
        }
        else
        {
            StatusText.Text = "Select a King or a face-up sequence to move it here.";
            RenderBoard();
        }
    }

    private void CompleteCardAction(bool moved)
    {
        if (moved)
        {
            _hasStarted = true;
            _clock.Start();
        }
        RenderBoard();
        UpdateStats();
        CheckForWin();
    }

    private void OnFoundationClick(int index)
    {
        if ((uint)index >= (uint)_game.Foundations.Length)
        {
            StatusText.Text = "That foundation is unavailable. Please try again.";
            return;
        }

        if (_selected is { } selected)
        {
            var moved = _game.TryMoveToFoundation(selected, index);
            _selected = null;
            StatusText.Text = moved ? "Card moved to foundation." : "Foundations build up by suit, starting with an Ace.";
            CompleteCardAction(moved);
            return;
        }
        else if (_selectedFoundation == index)
        {
            _selectedFoundation = -1;
            StatusText.Text = "Selection cleared.";
        }
        else if (_game.Foundations[index].Count > 0)
        {
            _selectedFoundation = _selectedFoundation == index ? -1 : index;
            StatusText.Text = _selectedFoundation >= 0
                ? "Foundation card selected. Choose a tableau column."
                : "Selection cleared.";
        }
        else
        {
            _selectedFoundation = -1;
            StatusText.Text = "Move an Ace here to start a foundation.";
        }

        RenderBoard();
        UpdateStats();
        CheckForWin();
    }

    private void OnStockClick()
    {
        _selected = null;
        _selectedFoundation = -1;
        var stockWasEmpty = _game.Stock.Count == 0;
        var wasteCountBeforeDraw = _game.Waste.Count;
        if (_game.DrawFromStock())
        {
            _hasStarted = true;
            _clock.Start();
            var drawnCardCount = stockWasEmpty ? 0 : _game.Waste.Count - wasteCountBeforeDraw;
            StatusText.Text = _game.Stock.Count == 0
                ? "Stock recycled. Keep building your sequences."
                : $"Drew {_game.DrawCount} card{(_game.DrawCount == 1 ? "" : "s")}.";
            RenderBoard();
            if (drawnCardCount > 0)
                AnimateWasteDeal(wasteCountBeforeDraw, _game.Waste.Count);
            else
                AnimateStockRecycle();
        }
        else
        {
            StatusText.Text = "No cards left in the stock. Try moving cards from the tableau.";
            RenderBoard();
        }
        UpdateStats();
    }

    private void AnimateWasteDeal(int firstCardIndex, int endCardIndex)
    {
        var cardWidth = Math.Min(CardWidthLimit, (BoardCanvas.ActualWidth - 44 - 72) / 7);
        var stockToWasteOffset = -(cardWidth + 12);
        for (var index = firstCardIndex; index < endCardIndex; index++)
        {
            var source = new CardSource(PileKind.Waste, 0, index);
            var card = BoardCanvas.Children.OfType<Border>()
                .FirstOrDefault(child => child.Tag is CardSource cardSource && cardSource == source);
            if (card is null)
                continue;

            var transform = new TranslateTransform(stockToWasteOffset, -8);
            card.RenderTransform = transform;
            card.Opacity = 0;
            var beginTime = TimeSpan.FromMilliseconds((index - firstCardIndex) * 75);
            var duration = TimeSpan.FromMilliseconds(260);
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            transform.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(stockToWasteOffset, 0, duration) { BeginTime = beginTime, EasingFunction = easing });
            transform.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(-8, 0, duration) { BeginTime = beginTime, EasingFunction = easing });
            card.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, duration) { BeginTime = beginTime });
        }
    }

    private void AnimateStockRecycle()
    {
        var stockBacks = BoardCanvas.Children.OfType<Border>()
            .Where(child => Equals(child.Tag, "StockBack"))
            .ToArray();
        for (var index = 0; index < stockBacks.Length; index++)
        {
            var card = stockBacks[index];
            card.Opacity = 0;
            var duration = TimeSpan.FromMilliseconds(180);
            card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration)
            {
                BeginTime = TimeSpan.FromMilliseconds(index * 45)
            });
        }
    }

    private void NewGame_Click(object sender, RoutedEventArgs e)
    {
        _game.NewGame(_game.DrawCount);
        StartFreshClock();
        _selected = null;
        _selectedFoundation = -1;
        StatusText.Text = $"New game started. Seed {_game.Seed}.";
        RenderBoard();
        UpdateStats();
        UpdateModeMenu();
    }

    private void Restart_Click(object sender, RoutedEventArgs e)
    {
        _game.NewGame(_game.DrawCount, _game.Seed);
        StartFreshClock();
        _selected = null;
        _selectedFoundation = -1;
        StatusText.Text = $"Game restarted. Seed {_game.Seed}.";
        RenderBoard();
        UpdateStats();
    }

    private void DrawOne_Click(object sender, RoutedEventArgs e) => SetDrawMode(1);
    private void DrawThree_Click(object sender, RoutedEventArgs e) => SetDrawMode(3);

    private void SetDrawMode(int drawCount)
    {
        if (_game.DrawCount == drawCount)
            return;
        _game.NewGame(drawCount);
        StartFreshClock();
        _selected = null;
        _selectedFoundation = -1;
        StatusText.Text = $"New draw-{drawCount} game started. Seed {_game.Seed}.";
        RenderBoard();
        UpdateStats();
        UpdateModeMenu();
    }

    private void UpdateModeMenu()
    {
        DrawOneMenuItem.IsChecked = _game.DrawCount == 1;
        DrawThreeMenuItem.IsChecked = _game.DrawCount == 3;
        DrawModeText.Text = $"DRAW {_game.DrawCount}";
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_game.Undo())
        {
            _selected = null;
            _selectedFoundation = -1;
            StatusText.Text = "Last move undone.";
            RenderBoard();
            UpdateStats();
            CheckForWin();
        }
        else
        {
            StatusText.Text = "There are no moves to undo.";
        }
    }

    private void Hint_Click(object sender, RoutedEventArgs e)
    {
        var hint = _game.FindHint();
        if (hint is null)
        {
            StatusText.Text = _game.Stock.Count > 0
                ? "No useful card move found. Try drawing from the stock."
                : "No new useful move found. Try another move or undo.";
            return;
        }

        _selected = hint.Value.Source;
        _selectedFoundation = -1;
        var destination = hint.Value.Destination == PileKind.Foundation
            ? "a foundation"
            : $"tableau column {hint.Value.DestinationIndex + 1}";
        StatusText.Text = $"Hint: move the highlighted card to {destination}.";
        RenderBoard();
    }

    private void HowToPlay_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this,
            "Build each foundation from Ace to King in the same suit. In the tableau, build down in alternating colors. Move a face-up sequence together, and place only Kings in empty columns. Click the stock to draw cards. Select a card, then its destination. Double-click a card to send it to a foundation when possible.",
            "How to Play Klondike", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this,
            "PatienceX\nA classic Klondike solitaire game for Windows.\n\nOriginal implementation and visual design.",
            "About PatienceX", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void BoardCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => RenderBoard();

    private void StartFreshClock()
    {
        _clock.Reset();
        _hasStarted = false;
        WinBanner.Visibility = Visibility.Collapsed;
        NoMovesBanner.Visibility = Visibility.Collapsed;
        UpdateStats();
    }

    private void UpdateStats()
    {
        ScoreText.Text = _game.Score.ToString();
        MovesText.Text = _game.Moves.ToString();
        var elapsed = _hasStarted ? _clock.Elapsed : TimeSpan.Zero;
        TimeText.Text = $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";
    }

    private void CheckForWin()
    {
        if (_game.IsWon)
        {
            NoMovesBanner.Visibility = Visibility.Collapsed;
            _clock.Stop();
            StatusText.Text = $"You won in {_game.Moves} moves. Congratulations!";
            WinBanner.Visibility = Visibility.Visible;
            return;
        }

        WinBanner.Visibility = Visibility.Collapsed;
        if (_game.IsDeadlocked)
        {
            _clock.Stop();
            StatusText.Text = "No more legal moves are available. Undo a move or start a new game.";
            NoMovesBanner.Visibility = Visibility.Visible;
            return;
        }

        NoMovesBanner.Visibility = Visibility.Collapsed;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.F2)
        {
            NewGame_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.H)
        {
            Hint_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Undo_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else
        {
            base.OnKeyDown(e);
        }
    }
}
