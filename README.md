# PatienceX

PatienceX is a native Windows Klondike solitaire game built with WPF. It features draw-one and draw-three games, animated card dragging and stock deals, expanded face-up tableau spacing, strategic hints, win and no-more-moves detection, deterministic game seeds, undo, scoring, a timer, and a custom card-table interface.

## Build and run

On Windows, install the .NET 8 SDK and run:

```powershell
dotnet run
```

To publish a self-contained 64-bit Windows build:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true
```

## Controls

- Click the stock to draw or recycle cards.
- Select a card or face-up sequence, then click a legal destination.
- Double-click a movable card to send it to its foundation when possible.
- Use **F2** for a new game, **H** for a hint, and **Ctrl+Z** to undo.
- Choose draw-one or draw-three from the **Game** menu.
