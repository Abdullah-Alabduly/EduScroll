# ADR-005: Flutter State Management Selection

- **Status:** PROPOSED — Pending Product Owner Approval (`DEC-P05`, `TBD-022`)
- **Date:** 2026-11-17

## 1. Decision Description
WP-05 requires proposing a maintainable Flutter architecture without assuming a state management package has already been selected.

## 2. Why It Matters
Consistent state management governs auth session reactivity, polling timers, vertical video controller lifecycles, and widget/unit testability across Sprints 3, 9, and 10.

## 3. Alternatives Considered
1. **Riverpod:** Compile-time safe provider graph, native `AsyncValue` modeling for loading/error/data states, clean dependency overriding in widget/unit tests, no `BuildContext` coupling.
2. **BLoC / Cubit:** Strict event/state separation, strong team conventions and test tooling (`bloc_test`), slightly higher boilerplate for simple CRUD screens.
3. **Provider + ChangeNotifier:** Minimal learning curve, built on `InheritedWidget`, but weaker compile-time safety and prone to boilerplate/manual disposal bugs on async flows.

## 4. Trade-Offs
- **Riverpod** minimizes boilerplate for async REST/polling workflows while remaining strongly testable.
- **BLoC/Cubit** is equally valid if the mobile team already has strong BLoC experience.

## 5. Recommendation
**PROPOSED:** Recommend **Riverpod** (or **BLoC/Cubit** if preferred by the implementing Flutter team) to be confirmed before Sprint 3 begins.

## 6. Approval Status
**Pending Product Owner Approval (`DEC-P05`, `TBD-022`).**
