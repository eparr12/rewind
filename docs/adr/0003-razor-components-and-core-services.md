# ADR-0003: Razor components and Core services, no MVVM Toolkit

- **Status:** Accepted
- **Date:** 2026-10-21
- **Author:** Ethan

## Context

Rewind's screens (Checkout, Return, Ask the Clerk, Intake and Store Manager) need somewhere to keep their UI state and somewhere to keep the store's rules. The rules, such as money, late fees, business days and barcode types, must be testable without a UI and must outlive any particular UI host (ADR-0002).

A WinForms or WPF developer might reach for MVVM with view models and data binding, often through CommunityToolkit.Mvvm. Blazor components, however, already re-render from their own fields, and they get services through dependency injection.

## Decision

- **Rules and use cases live in Rewind.Core services.** A component calls one Core service per user action and renders its result. Components never query EF Core directly (ARCH-5).
- **A component's own fields hold its UI state.** No MVVM framework and no view models by default.
- **A screen that outgrows its fields moves that state into a plain C# class in Rewind.UI.** It has no base class, no `INotifyPropertyChanged` and no source generators. The component calls `StateHasChanged` when the class changes.
- **Shared app-wide UI state**, such as the current dialog or device status, lives in small singleton services, such as `DialogService`, that raise a `Changed` event. Components subscribe to it and refresh through `InvokeAsync`.

## Alternatives

- **CommunityToolkit.Mvvm view models.** These are well made, but they add a package and source generators, and they duplicate what Blazor's rendering already does. Binding `INotifyPropertyChanged` into Blazor needs extra glue code in every component.
- **A state-management library such as Fluxor.** Actions, reducers and stores are far more ceremony than five screens and one clerk need.
- **Logic in the components.** This is fastest at first, but the rules would be hard to test, and they couldn't move to another host.

## Consequences

- Fewer dependencies to keep alive for 20 years (GEN-9).
- Core rules are tested with xUnit and no UI, and screens are tested with bUnit against fake services.
- Components stay thin. A component that grows long is a sign to move logic into Core or state into a plain class.
- A reviewer checks that no component injects a `DbContext` or `IDbContextFactory`. NetArchTest can enforce this later (ARCH-7).
