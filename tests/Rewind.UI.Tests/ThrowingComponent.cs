using Microsoft.AspNetCore.Components;

namespace Rewind.UI.Tests;

/// <summary>Stands in for a screen or a part of the shell that throws as soon as it opens.</summary>
internal sealed class ThrowingComponent : ComponentBase
{
    protected override void OnInitialized() => throw new InvalidOperationException("Test crash");
}
