# Lakona Arena visual QA

## Evidence

- Selected direction: option 2, arcade broadcast visual language.
- Reference: `C:\Users\bruce\.codex\generated_images\019f56af-7f3a-7f61-b195-ff422ade0f41\exec-9733caec-6f9d-434b-99de-b9a491019833.png`
- Unity implementation capture: `C:\Users\bruce\Documents\GitHub\lakona\.tmp\lakona-e2e-signal-unity\unity-login.png`
- Side-by-side comparison: `C:\Users\bruce\.codex\visualizations\2026\07\12\019f56af-7f3a-7f61-b195-ff422ade0f41\arena-design-qa-comparison.png`
- Captured state: generated Unity client, login screen, 1155 × 511 compact Game view.

## Iterations

1. The initial IMGUI battlefield covered the UI Toolkit login surface. The battlefield was moved into the same UI Toolkit render tree with `Painter2D`, restoring deterministic layering.
2. The first integrated capture clipped the title and status text in a short Unity Game view. Title scale and panel position were corrected, and a compact layout was added for viewports below 600 px high.
3. A preview HUD was added for normal-height login screens to match the broadcast framing. It automatically collapses in compact mode so the callsign input and primary action remain unobstructed.

## Final assessment

- Hierarchy: passed. The product name, callsign prompt, input, and primary action have an unambiguous reading order.
- Color and contrast: passed. Warm ivory, acid lime, coral, and charcoal match the selected direction; input text remains visible against the field background.
- Spacing and responsive behavior: passed. The compact Unity view has no clipped primary controls or overlapping HUD.
- Gameplay language: passed. Arena grid, targeting rings, players, monsters, projectile streaks, segmented health, and hit rings form one coherent system.
- Cross-engine parity: passed. Unity/UnityCn/Tuanjie share the UI Toolkit implementation; Godot uses the same layout, palette, HUD, and native canvas primitives.
- Copy: passed. Login and gameplay labels are concise and consistent.
- Remaining low-priority variance: the native Unity field uses helper copy below the control instead of the reference's in-field icon/placeholder treatment.

## Result

passed
