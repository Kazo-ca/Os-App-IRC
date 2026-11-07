using System.Globalization;
using Kazo.Irc.Chat;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Kazo.Irc.Components;

/// <summary>
/// The conversation: its lines, newest at the bottom, and the input line with its history and
/// nickname completion (decision 18).
/// </summary>
public sealed partial class MessagePane
{
    /// <summary>The lines rendered; older ones stay in memory and in the logs.</summary>
    public const int RenderedLines = 400;

    private readonly InputHistory _history = new();
    private readonly NickCompleter _completer = new();
    private ElementReference _input;
    private IJSObjectReference? _module;
    private string _text = string.Empty;

    /// <summary>Gets or sets the conversation shown, or null for the empty state.</summary>
    [Parameter]
    public Conversation? Conversation { get; set; }

    /// <summary>Gets or sets the title of the empty state.</summary>
    [Parameter, EditorRequired]
    public string EmptyTitle { get; set; } = string.Empty;

    /// <summary>Gets or sets the line under the title of the empty state.</summary>
    [Parameter, EditorRequired]
    public string EmptyHint { get; set; } = string.Empty;

    /// <summary>Gets or sets the accessible name of the input line.</summary>
    [Parameter, EditorRequired]
    public string InputLabel { get; set; } = string.Empty;

    /// <summary>Gets or sets the placeholder of the input line.</summary>
    [Parameter, EditorRequired]
    public string InputPlaceholder { get; set; } = string.Empty;

    /// <summary>Gets or sets the text of the send button.</summary>
    [Parameter, EditorRequired]
    public string SendLabel { get; set; } = string.Empty;

    /// <summary>Gets or sets whether the user can type (a network is open).</summary>
    [Parameter]
    public bool InputEnabled { get; set; }

    /// <summary>
    /// Gets or sets what sending a line does; it returns true when the line was taken (the input is
    /// then emptied and the line goes to the history).
    /// </summary>
    [Parameter]
    public Func<string, Task<bool>>? OnSubmit { get; set; }

    /// <summary>Gets or sets the text of an activity line (join, part, nickname...).</summary>
    [Parameter]
    public Func<ChatLine, string> ActivityText { get; set; } = line => $"{line.Nick} {line.Text}";

    /// <summary>Gets or sets what a click on a web address of a message does (work item 5).</summary>
    [Parameter]
    public Func<Uri, Task>? OnOpenLink { get; set; }

    /// <summary>Gets or sets the time zone of the times shown (the OS's).</summary>
    [Parameter]
    public TimeZoneInfo TimeZone { get; set; } = TimeZoneInfo.Utc;

    private IEnumerable<ChatLine> VisibleLines =>
        Conversation is null ? [] : Conversation.Lines.Skip(Math.Max(0, Conversation.Lines.Count - RenderedLines));

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            try
            {
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // The page is gone.
            }
        }
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            // Tab completes a nickname instead of moving the focus, but only while the input holds
            // text: an empty input lets Tab leave it, so the keyboard is never trapped.
            _module = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/Kazo.Irc/Components/MessagePane.razor.js");
            await _module.InvokeVoidAsync("keepTabForCompletion", _input);
        }
    }

    private string FormatTime(DateTimeOffset time) =>
        TimeZoneInfo.ConvertTime(time, TimeZone).ToString("HH:mm", CultureInfo.InvariantCulture);

    private void OnInput(ChangeEventArgs e)
    {
        _text = e.Value?.ToString() ?? string.Empty;
        _completer.Reset();
    }

    private Task OnKeyDownAsync(KeyboardEventArgs e)
    {
        switch (e.Key)
        {
            case "ArrowUp":
                _text = _history.Previous(_text);
                break;
            case "ArrowDown":
                _text = _history.Next(_text);
                break;
            case "Tab" when !e.ShiftKey && _text.Length > 0:
                var nicks = Conversation?.Users.Select(user => user.Nick)
                    ?? [];
                if (Conversation?.Kind == ConversationKind.Private)
                {
                    nicks = nicks.Append(Conversation.Name);
                }

                _text = _completer.Complete(_text, nicks);
                break;
        }

        return Task.CompletedTask;
    }

    private async Task SubmitAsync()
    {
        if (OnSubmit is null || string.IsNullOrWhiteSpace(_text))
        {
            return;
        }

        var line = _text;
        if (await OnSubmit(line))
        {
            _history.Add(line);
            _text = string.Empty;
            _completer.Reset();
        }
    }
}
