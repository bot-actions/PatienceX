namespace PatienceX;

public enum Suit
{
    Clubs,
    Diamonds,
    Hearts,
    Spades
}

public enum PileKind
{
    Waste,
    Tableau,
    Foundation
}

public readonly record struct CardSource(PileKind Kind, int PileIndex = 0, int CardIndex = 0);

public sealed class Card
{
    public Card(Suit suit, int rank, bool faceUp = false)
    {
        Suit = suit;
        Rank = rank;
        FaceUp = faceUp;
    }

    public Suit Suit { get; }
    public int Rank { get; }
    public bool FaceUp { get; set; }
    public bool IsRed => Suit is Suit.Diamonds or Suit.Hearts;
    public string RankLabel => Rank switch
    {
        1 => "A",
        11 => "J",
        12 => "Q",
        13 => "K",
        _ => Rank.ToString()
    };
    public string SuitSymbol => Suit switch
    {
        Suit.Clubs => "♣",
        Suit.Diamonds => "♦",
        Suit.Hearts => "♥",
        _ => "♠"
    };

    public Card Copy() => new(Suit, Rank, FaceUp);
}

public readonly record struct SuggestedMove(CardSource Source, PileKind Destination, int DestinationIndex);

public sealed class GameEngine
{
    private readonly Stack<GameSnapshot> _history = new();
    private int _seed;
    private MoveRecord? _lastMove;

    public List<Card> Stock { get; private set; } = [];
    public List<Card> Waste { get; private set; } = [];
    public List<Card>[] Foundations { get; private set; } = [];
    public List<Card>[] Tableau { get; private set; } = [];
    public int DrawCount { get; private set; } = 1;
    public int Score { get; private set; }
    public int Moves { get; private set; }
    public int Seed => _seed;
    public bool HasUndo => _history.Count > 0;
    public bool IsWon => Foundations.Length == 4 && Foundations.All(pile => pile.Count == 13);
    public bool IsDeadlocked => !IsWon && !HasAnyLegalMoves();

    public void NewGame(int drawCount = 1, int? seed = null)
    {
        DrawCount = drawCount is 3 ? 3 : 1;
        _seed = seed ?? Random.Shared.Next(1, int.MaxValue);
        _history.Clear();
        _lastMove = null;
        Score = 0;
        Moves = 0;
        Stock = [];
        Waste = [];
        Foundations = Enumerable.Range(0, 4).Select(_ => new List<Card>()).ToArray();
        Tableau = Enumerable.Range(0, 7).Select(_ => new List<Card>()).ToArray();

        var deck = (from suit in Enum.GetValues<Suit>()
                    from rank in Enumerable.Range(1, 13)
                    select new Card(suit, rank)).ToList();
        var random = new Random(_seed);
        for (var i = deck.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }

        var nextCard = 0;
        for (var column = 0; column < Tableau.Length; column++)
        {
            for (var row = 0; row <= column; row++)
            {
                var card = deck[nextCard++];
                card.FaceUp = row == column;
                Tableau[column].Add(card);
            }
        }

        for (; nextCard < deck.Count; nextCard++)
        {
            deck[nextCard].FaceUp = false;
            Stock.Add(deck[nextCard]);
        }
    }

    public bool DrawFromStock()
    {
        if (Stock.Count == 0 && Waste.Count == 0)
            return false;

        SaveSnapshot();
        if (Stock.Count == 0)
        {
            for (var i = Waste.Count - 1; i >= 0; i--)
            {
                Waste[i].FaceUp = false;
                Stock.Add(Waste[i]);
            }
            Waste.Clear();
            Score = Math.Max(0, Score - 100);
            Moves++;
            _lastMove = null;
            return true;
        }

        var drawCount = Math.Min(DrawCount, Stock.Count);
        for (var i = 0; i < drawCount; i++)
        {
            var card = Stock[^1];
            Stock.RemoveAt(Stock.Count - 1);
            card.FaceUp = true;
            Waste.Add(card);
        }
        Moves++;
        _lastMove = null;
        return true;
    }

    public Card? GetCard(CardSource source)
    {
        var pile = GetPile(source.Kind, source.PileIndex);
        return source.CardIndex >= 0 && source.CardIndex < pile.Count ? pile[source.CardIndex] : null;
    }

    public bool IsMovableSource(CardSource source)
    {
        var card = GetCard(source);
        if (card is null || !card.FaceUp)
            return false;

        return source.Kind switch
        {
            PileKind.Waste => source.CardIndex == Waste.Count - 1,
            PileKind.Foundation => source.CardIndex == Foundations[source.PileIndex].Count - 1,
            PileKind.Tableau => source.CardIndex >= 0 &&
                                source.CardIndex < Tableau[source.PileIndex].Count &&
                                Tableau[source.PileIndex].Skip(source.CardIndex).All(item => item.FaceUp) &&
                                source.CardIndex == Tableau[source.PileIndex].FindIndex(item => ReferenceEquals(item, card)),
            _ => false
        };
    }

    public bool TryMoveToTableau(CardSource source, int destinationIndex)
    {
        if (!IsMovableSource(source) || destinationIndex < 0 || destinationIndex >= Tableau.Length)
            return false;
        if (source.Kind == PileKind.Tableau && source.PileIndex == destinationIndex)
            return false;

        var card = GetCard(source)!;
        var destination = Tableau[destinationIndex];
        if (destination.Count == 0 ? card.Rank != 13 :
            destination[^1].Rank != card.Rank + 1 || destination[^1].IsRed == card.IsRed)
            return false;

        SaveSnapshot();
        var destinationStartIndex = destination.Count;
        var movingCards = RemoveSourceCards(source);
        destination.AddRange(movingCards);
        _lastMove = new MoveRecord(source, PileKind.Tableau, destinationIndex,
            destinationStartIndex, movingCards.Count, movingCards[0].Suit, movingCards[0].Rank);
        FinishMove();
        return true;
    }

    public bool TryMoveToFoundation(CardSource source, int foundationIndex)
    {
        if (!IsMovableSource(source) || foundationIndex < 0 || foundationIndex >= Foundations.Length)
            return false;
        if (source.Kind == PileKind.Tableau && source.CardIndex != Tableau[source.PileIndex].Count - 1)
            return false;
        if (source.Kind == PileKind.Foundation)
            return false;

        var card = GetCard(source)!;
        var destination = Foundations[foundationIndex];
        if (destination.Count == 0
                ? card.Rank != 1
                : destination[^1].Suit != card.Suit || destination[^1].Rank + 1 != card.Rank)
            return false;

        SaveSnapshot();
        var destinationStartIndex = destination.Count;
        var movingCards = RemoveSourceCards(source);
        destination.AddRange(movingCards);
        _lastMove = new MoveRecord(source, PileKind.Foundation, foundationIndex,
            destinationStartIndex, movingCards.Count, movingCards[0].Suit, movingCards[0].Rank);
        Score += 10;
        FinishMove();
        return true;
    }

    public bool TryAutoMoveToFoundation(CardSource source)
    {
        if (!IsMovableSource(source))
            return false;
        var card = GetCard(source)!;
        var foundationIndex = FindFoundationForCard(card);
        return foundationIndex >= 0 && TryMoveToFoundation(source, foundationIndex);
    }

    public bool Undo()
    {
        if (!_history.TryPop(out var snapshot))
            return false;

        Stock = CopyPile(snapshot.Stock);
        Waste = CopyPile(snapshot.Waste);
        Foundations = snapshot.Foundations.Select(CopyPile).ToArray();
        Tableau = snapshot.Tableau.Select(CopyPile).ToArray();
        Score = snapshot.Score;
        Moves = snapshot.Moves;
        _lastMove = snapshot.LastMove;
        return true;
    }

    public SuggestedMove? FindHint()
    {
        if (Waste.Count > 0)
        {
            var wasteSource = new CardSource(PileKind.Waste, 0, Waste.Count - 1);
            if (CanMoveToFoundation(wasteSource))
                return new SuggestedMove(wasteSource, PileKind.Foundation, FindFoundationForCard(Waste[^1]));
        }

        for (var column = 0; column < Tableau.Length; column++)
        {
            if (Tableau[column].Count == 0)
                continue;
            var source = new CardSource(PileKind.Tableau, column, Tableau[column].Count - 1);
            if (CanMoveToFoundation(source))
                return new SuggestedMove(source, PileKind.Foundation, FindFoundationForCard(Tableau[column][^1]));
        }

        var visibleSources = new List<CardSource>();
        if (Waste.Count > 0)
            visibleSources.Add(new CardSource(PileKind.Waste, 0, Waste.Count - 1));
        for (var column = 0; column < Tableau.Length; column++)
        {
            for (var row = 0; row < Tableau[column].Count; row++)
            {
                if (Tableau[column][row].FaceUp)
                    visibleSources.Add(new CardSource(PileKind.Tableau, column, row));
            }
        }

        for (var foundation = 0; foundation < Foundations.Length; foundation++)
        {
            if (Foundations[foundation].Count > 0)
                visibleSources.Add(new CardSource(PileKind.Foundation, foundation, Foundations[foundation].Count - 1));
        }

        var tableauMoves = new List<(int Priority, SuggestedMove Move)>();
        foreach (var source in visibleSources)
        {
            var card = GetCard(source)!;
            for (var column = 0; column < Tableau.Length; column++)
            {
                if (!CanMoveToTableau(source, column) ||
                    IsImmediateReverse(source, PileKind.Tableau, column))
                    continue;

                var destinationIsEmpty = Tableau[column].Count == 0;
                var revealsFaceDownCard = source.Kind == PileKind.Tableau &&
                    source.CardIndex > 0 &&
                    !Tableau[source.PileIndex][source.CardIndex - 1].FaceUp;
                var priority = revealsFaceDownCard ? 0 :
                    destinationIsEmpty && card.Rank == 13 ? 1 :
                    source.Kind == PileKind.Waste ? 2 :
                    source.Kind == PileKind.Tableau ? 3 : 4;
                tableauMoves.Add((priority, new SuggestedMove(source, PileKind.Tableau, column)));
            }
        }

        return tableauMoves
            .OrderBy(candidate => candidate.Priority)
            .Select(candidate => (SuggestedMove?)candidate.Move)
            .FirstOrDefault();
    }

    private bool CanMoveToTableau(CardSource source, int destinationIndex)
    {
        if (!IsMovableSource(source) || destinationIndex < 0 || destinationIndex >= Tableau.Length)
            return false;
        if (source.Kind == PileKind.Tableau && source.PileIndex == destinationIndex)
            return false;

        var card = GetCard(source)!;
        var destination = Tableau[destinationIndex];
        return destination.Count == 0
            ? card.Rank == 13
            : destination[^1].Rank == card.Rank + 1 && destination[^1].IsRed != card.IsRed;
    }

    private bool IsImmediateReverse(CardSource source, PileKind destination, int destinationIndex)
    {
        if (_lastMove is not { } previous ||
            previous.Destination != source.Kind ||
            previous.DestinationIndex != source.PileIndex ||
            destination != previous.Source.Kind ||
            destinationIndex != previous.Source.PileIndex)
            return false;

        var sourcePile = GetPile(source.Kind, source.PileIndex);
        if (source.CardIndex != previous.DestinationStartIndex ||
            source.CardIndex + previous.CardCount != sourcePile.Count)
            return false;

        var firstCard = sourcePile[source.CardIndex];
        return firstCard.Suit == previous.FirstCardSuit &&
               firstCard.Rank == previous.FirstCardRank;
    }

    private bool HasAnyLegalMoves()
    {
        if (Stock.Count > 0 || Waste.Count > 0)
            return true;

        for (var column = 0; column < Tableau.Length; column++)
        {
            var pile = Tableau[column];
            var firstFaceUp = pile.FindIndex(card => card.FaceUp);
            if (firstFaceUp < 0)
                continue;

            for (var row = firstFaceUp; row < pile.Count; row++)
            {
                var card = pile[row];
                if (row == pile.Count - 1 && CanMoveCardToFoundation(card))
                    return true;

                for (var destination = 0; destination < Tableau.Length; destination++)
                {
                    if (destination != column && CanPlaceOnTableau(card, Tableau[destination]))
                        return true;
                }
            }
        }

        foreach (var foundation in Foundations)
        {
            if (foundation.Count > 0 &&
                Tableau.Any(pile => CanPlaceOnTableau(foundation[^1], pile)))
                return true;
        }

        return false;
    }

    private bool CanMoveCardToFoundation(Card card)
    {
        foreach (var foundation in Foundations)
        {
            if (foundation.Count == 0 ? card.Rank == 1 :
                foundation[^1].Suit == card.Suit && foundation[^1].Rank + 1 == card.Rank)
                return true;
        }

        return false;
    }

    private static bool CanPlaceOnTableau(Card card, List<Card> destination) =>
        destination.Count == 0
            ? card.Rank == 13
            : destination[^1].Rank == card.Rank + 1 && destination[^1].IsRed != card.IsRed;

    private bool CanMoveToFoundation(CardSource source)
    {
        if (!IsMovableSource(source) || source.Kind == PileKind.Foundation)
            return false;
        if (source.Kind == PileKind.Tableau && source.CardIndex != Tableau[source.PileIndex].Count - 1)
            return false;

        var card = GetCard(source)!;
        var foundationIndex = FindFoundationForCard(card);
        if (foundationIndex < 0)
            return false;
        var foundation = Foundations[foundationIndex];
        return foundation.Count == 0
            ? card.Rank == 1
            : foundation[^1].Suit == card.Suit && foundation[^1].Rank + 1 == card.Rank;
    }

    private int FindFoundationForCard(Card card)
    {
        for (var i = 0; i < Foundations.Length; i++)
        {
            if (Foundations[i].Count > 0 && Foundations[i][0].Suit == card.Suit)
                return i;
        }

        for (var i = 0; i < Foundations.Length; i++)
        {
            if (Foundations[i].Count == 0)
                return i;
        }

        return -1;
    }

    private List<Card> RemoveSourceCards(CardSource source)
    {
        var pile = GetPile(source.Kind, source.PileIndex);
        var cards = pile.GetRange(source.CardIndex, pile.Count - source.CardIndex);
        pile.RemoveRange(source.CardIndex, pile.Count - source.CardIndex);
        if (source.Kind == PileKind.Tableau && pile.Count > 0 && !pile[^1].FaceUp)
        {
            pile[^1].FaceUp = true;
            Score += 5;
        }
        return cards;
    }

    private void FinishMove()
    {
        Moves++;
    }

    private void SaveSnapshot()
    {
        _history.Push(new GameSnapshot(
            CopyPile(Stock),
            CopyPile(Waste),
            Foundations.Select(CopyPile).ToArray(),
            Tableau.Select(CopyPile).ToArray(),
            Score,
            Moves,
            _lastMove));
    }

    private List<Card> GetPile(PileKind kind, int index) => kind switch
    {
        PileKind.Waste => Waste,
        PileKind.Tableau => index >= 0 && index < Tableau.Length ? Tableau[index] : [],
        PileKind.Foundation => index >= 0 && index < Foundations.Length ? Foundations[index] : [],
        _ => []
    };

    private static List<Card> CopyPile(IEnumerable<Card> cards) => cards.Select(card => card.Copy()).ToList();

    private sealed record GameSnapshot(
        List<Card> Stock,
        List<Card> Waste,
        List<Card>[] Foundations,
        List<Card>[] Tableau,
        int Score,
        int Moves,
        MoveRecord? LastMove);

    private sealed record MoveRecord(
        CardSource Source,
        PileKind Destination,
        int DestinationIndex,
        int DestinationStartIndex,
        int CardCount,
        Suit FirstCardSuit,
        int FirstCardRank);
}
