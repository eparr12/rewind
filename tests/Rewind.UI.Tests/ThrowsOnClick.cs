using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Rewind.UI.Tests;

/// <summary>
/// Stands in for a part of the shell outside the screens, such as the status bar: it draws
/// fine, but its button throws.
/// </summary>
internal sealed class ThrowsOnClick : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "button");
        builder.AddAttribute(1, "class", "throws");
        builder.AddAttribute(2, "onclick", EventCallback.Factory.Create(this, Throw));
        builder.CloseElement();
    }

    private static void Throw() => throw new InvalidOperationException("Test crash outside the screens");
}
