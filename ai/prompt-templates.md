# Canonical workflows

Use the smallest workflow that fits the request. In every workflow, inspect repository reality first and preserve unrelated work.

## FULL

Use for an end-to-end feature or milestone.

1. Read `AI.md` and relevant `ai/` maps.
2. Inspect code, scenes, settings, tests, and Git state.
3. State scope, assumptions, risks, and acceptance checks.
4. Implement in small coherent slices.
5. Compile and run proportionate EditMode, PlayMode, and manual checks.
6. Review the diff for accidental generated or unrelated changes.
7. Update architecture/status maps, work log, and release notes where applicable.
8. Report the outcome, verification evidence, and remaining limitations.

## PLAN

Use when the deliverable is a plan only.

1. Inspect project reality and relevant references.
2. Identify affected systems, dependencies, risks, and open decisions.
3. Define ordered implementation slices and objective acceptance checks.
4. Clearly separate current state, assumptions, and proposed state.
5. Do not modify project files unless the user explicitly expands the scope.

## BUGFIX

Use for incorrect existing behavior.

1. Reproduce or establish concrete evidence of the failure.
2. Trace the smallest root cause; do not patch symptoms blindly.
3. Add a focused regression test when practical.
4. Apply the minimal safe fix.
5. Re-run the reproduction and nearby tests.
6. Record only architecture or player-visible changes that actually occurred.

## REFACTOR

Use for behavior-preserving structural work.

1. Document the behavior and tests that must remain unchanged.
2. Identify boundaries and downstream callers before editing.
3. Refactor in reviewable steps without mixing new features.
4. Compile and run the existing behavior checks after each risky boundary change.
5. Update system maps only when ownership or dependencies changed.

