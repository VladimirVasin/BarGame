# Canonical workflows

Use the smallest workflow that fits the request. In every workflow, inspect repository reality first and preserve unrelated work.

## FAST (default)

Use for ordinary implementation, including end-to-end features and shared
runtime changes.

1. Inspect only the affected code, data and current Git state.
2. Implement one coherent batch.
3. Run one primary verification check:
   - documentation: diff review plus `git diff --check`;
   - tooling/data/art: the directly affected validator;
   - C# code with a suitable focused test: run that EditMode or PlayMode
     selection; it already compiles dependencies, so do not build separately;
   - C# code without a suitable test: build only the highest affected project;
   - scene/serialization/build settings: choose one affected scene test, manual
     smoke or requested player build, rather than stacking them.
4. Use one additional focused check only for a shared-framework change that the
   primary check cannot cover. Default to one Unity invocation; cap shared
   framework work at two narrowly filtered invocations.
5. Stop once the focused check provides sufficient evidence. Do not add a
   complete suite by default; use RELEASE VERIFICATION only when the user
   explicitly requests it. A known unrelated failure does not authorize a
   broad rerun or an unrelated repair. Create a player build only when it is
   the requested deliverable or release gate; add a smoke only when requested
   or when packaged startup behavior is the changed contract.
6. `-testFilter` is a REGULAR EXPRESSION, not a prefix. `"Bar"` matches every
   test in the project through the `BarPromenade` namespace; a filter intended
   for `96` tests silently ran `1704`. Always read `total` back out of the
   results file and check it is the number you meant.
7. Art and geometry are accepted by LOOKING, not by counting. Numbers cannot
   see a misplaced object or a mesh at a hundredth of its size: three such
   defects passed `1710` green tests in one session and were caught only by a
   rendered frame. Capture frames for any scene whose appearance changed.
8. Treat 30 seconds without useful progress as a soft timeout: inspect the
   process instead of repeating long polls. If the user will test/build
   manually, stop the matching automated process and hand off exact files.
9. Report the result and mention omitted broad checks in one sentence.

## FEATURE

Use for planning and implementing an end-to-end feature. This describes scope,
not a request for full regression.

1. Read `AI.md` and relevant `ai/` maps.
2. Inspect code, scenes, settings, tests, and Git state.
3. State scope, assumptions, risks, and acceptance checks.
4. Implement in small coherent slices.
5. Apply the FAST verification budget once after the coherent implementation
   batch.
6. Review the diff for accidental generated or unrelated changes.
7. Update architecture/status maps, work log, and release notes where applicable.
8. Report the outcome, verification evidence, and remaining limitations.

## RELEASE VERIFICATION

Use only when the user explicitly asks for a full regression or release gate.

1. Agree on the requested gate: full EditMode, full PlayMode, player build and/or
   smoke. Do not silently add the other gates.
2. Run each requested broad check once.
3. Re-run only failing tests that need classification; do not repeat a green
   suite.
4. Report exact counts, duration-significant omissions and known unrelated
   failures.

## PLAN

Use when the deliverable is a plan only.

1. Inspect project reality and relevant references.
2. Identify affected systems, dependencies, risks, and open decisions.
3. Define ordered implementation slices and a minimal acceptance budget: one
   primary check plus at most one focused behavior check.
4. Clearly separate current state, assumptions, and proposed state.
5. Do not include complete suites unless the user explicitly requests release
   verification. Include a player build or smoke only when it is explicitly
   requested as a deliverable or gate.
6. Do not modify project files unless the user explicitly expands the scope.

## BUGFIX

Use for incorrect existing behavior.

1. Reproduce or establish concrete evidence of the failure.
2. Trace the smallest root cause; do not patch symptoms blindly.
3. Add a focused regression test when practical.
4. Apply the minimal safe fix.
5. Re-run only the reproduction or its focused regression test.
6. Record only architecture or player-visible changes that actually occurred.

## REFACTOR

Use for behavior-preserving structural work.

1. Document the behavior and tests that must remain unchanged.
2. Identify boundaries and downstream callers before editing.
3. Refactor in reviewable steps without mixing new features.
4. After one coherent batch, run one focused behavior test when suitable; that
   run supplies compilation evidence. If no suitable test exists, build only
   the highest affected project.
5. Update system maps only when ownership or dependencies changed.
