namespace DotnetTestRunnerCli.Tui;

public sealed class InputHandler
{
    private bool _awaitingSecondG = false;
    private bool _searchMode = false;

    public void SetSearchMode(bool active) => _searchMode = active;

    public InputAction ReadNext()
    {
        var key = Console.ReadKey(intercept: true);
        return TranslateKey(key);
    }

    public InputAction TranslateKey(ConsoleKeyInfo key)
    {
        if (_searchMode)
            return TranslateSearchKey(key);

        return TranslateNormalKey(key);
    }

    private InputAction TranslateNormalKey(ConsoleKeyInfo key)
    {
        // Handle gg sequence
        if (_awaitingSecondG)
        {
            _awaitingSecondG = false;
            if (key.KeyChar == 'g' && key.Modifiers == 0)
                return new InputAction.MoveToTop();
            // Not gg — fall through and interpret this key normally
        }

        // Ctrl keys
        if (key.Modifiers == ConsoleModifiers.Control)
        {
            return key.Key switch
            {
                ConsoleKey.D => new InputAction.PageDown(),
                ConsoleKey.U => new InputAction.PageUp(),
                _ => new InputAction.Unknown()
            };
        }

        // Special keys
        if (key.Modifiers == 0)
        {
            switch (key.Key)
            {
                case ConsoleKey.DownArrow: return new InputAction.MoveDown(1);
                case ConsoleKey.UpArrow: return new InputAction.MoveUp(1);
                case ConsoleKey.PageDown: return new InputAction.PageDown();
                case ConsoleKey.PageUp: return new InputAction.PageUp();
                case ConsoleKey.Home: return new InputAction.MoveToTop();
                case ConsoleKey.End: return new InputAction.MoveToBottom();
                case ConsoleKey.Enter: return new InputAction.Run();
                case ConsoleKey.Spacebar: return new InputAction.Select();
                case ConsoleKey.Escape: return new InputAction.Quit();
                case ConsoleKey.F5: return new InputAction.Refresh();
                case ConsoleKey.Tab: return new InputAction.ToggleFilter();
            }
        }

        // Character keys
        return key.KeyChar switch
        {
            'j' => new InputAction.MoveDown(1),
            'k' => new InputAction.MoveUp(1),
            'G' => new InputAction.MoveToBottom(),
            'g' when key.Modifiers == 0 => SetAwaitingSecondG(),
            'e' => new InputAction.ToggleExpand(),
            'r' => new InputAction.Run(),
            '/' => new InputAction.ActivateSearch(),
            'n' => new InputAction.NextMatch(),
            'N' => new InputAction.PrevMatch(),
            ']' => new InputAction.DetailScrollDown(),
            '[' => new InputAction.DetailScrollUp(),
            'o' => new InputAction.DetailScrollRight(),
            'i' => new InputAction.DetailScrollLeft(),
            'q' => new InputAction.Quit(),
            _ => new InputAction.Unknown()
        };
    }

    private InputAction SetAwaitingSecondG()
    {
        _awaitingSecondG = true;
        return new InputAction.Unknown(); // Wait for next key
    }

    private InputAction TranslateSearchKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Enter: return new InputAction.SearchConfirm();
            case ConsoleKey.Escape: return new InputAction.SearchCancel();
            case ConsoleKey.Backspace: return new InputAction.SearchBackspace();
        }

        if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar))
            return new InputAction.SearchChar(key.KeyChar);

        return new InputAction.Unknown();
    }
}
