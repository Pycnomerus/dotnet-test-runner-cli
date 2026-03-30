namespace DotnetTestRunnerCli.Tui;

public abstract record InputAction
{
    public record MoveDown(int Delta) : InputAction;
    public record MoveUp(int Delta) : InputAction;
    public record MoveToTop : InputAction;
    public record MoveToBottom : InputAction;
    public record PageDown : InputAction;
    public record PageUp : InputAction;
    public record ToggleExpand : InputAction;
    public record Select : InputAction;
    public record Run : InputAction;
    public record ActivateSearch : InputAction;
    public record NextMatch : InputAction;
    public record PrevMatch : InputAction;
    public record Quit : InputAction;
    public record Refresh : InputAction;
    public record ToggleFilter : InputAction;
    public record DetailScrollDown : InputAction;
    public record DetailScrollUp : InputAction;
    public record DetailScrollRight : InputAction;
    public record DetailScrollLeft : InputAction;
    public record OpenInEditor : InputAction;
    public record SearchChar(char C) : InputAction;
    public record SearchBackspace : InputAction;
    public record SearchConfirm : InputAction;
    public record SearchCancel : InputAction;
    public record Unknown : InputAction;
}
