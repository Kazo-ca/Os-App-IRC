// Tab in the message input completes a nickname (MessagePane.razor.cs) instead of moving the focus.
// Blazor cannot cancel one key only, so this listener does it: only Tab without Shift, and only
// while the input holds text, so an empty input still lets the keyboard leave it.
export function keepTabForCompletion(input) {
    if (!input || input.dataset.ircTab) {
        return;
    }
    input.dataset.ircTab = '1';
    input.addEventListener('keydown', event => {
        if (event.key === 'Tab' && !event.shiftKey && input.value.length > 0) {
            event.preventDefault();
        }
    });
}
