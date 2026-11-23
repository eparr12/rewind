using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace Rewind.UI.Tests;

/// <summary>
/// Stands in for the screen around a form or a picker, with a key handler of its own, such as Checkout's Esc, which
/// asks to cancel the whole sale. A test renders the form or picker inside it to show which keys go no further.
/// </summary>
internal sealed class ScreenAround : ComponentBase
{
    [Parameter]
    public EventCallback<KeyboardEventArgs> OnKeyDown { get; set; }

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "onkeydown", OnKeyDown);
        builder.AddContent(2, ChildContent);
        builder.CloseElement();
    }
}
