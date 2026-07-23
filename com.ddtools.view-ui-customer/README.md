# DDTools View UI Customer

`ViewUICustomer<TView, TElement>` stores visual states for typed UI elements. Open **Tools > UI > View UI Customer Editor** to capture, preview, transition, import, or export states and to generate the view/helper scripts.

The package depends only on Unity UI and TextMesh Pro. `ViewUIButton` exposes pointer events but intentionally contains no project-specific audio behavior. Add a raycastable UI `Graphic` to objects that should receive pointer events.

The namespace remains `Core.Scripts.UI.Universal.ViewUICustomer` for serialization and source compatibility with existing Valhalla assets.

## Captured properties

Capturing a state stores the visible serialized properties of every component on a `ViewUIGetHelper` object. At runtime, values are compared between visual states per view and element type. Only properties that differ are applied; matching properties remain under the control of their original component or another system (for example, localization). Numeric, color, vector, and quaternion values are interpolated during transitions, while discrete values are applied when the transition completes.

`TMP_Text` content is intentionally excluded from captured and applied properties so localization and runtime text updates remain authoritative. Other text presentation properties can still participate in visual states.

Legacy active, position, size, color, and material fields remain supported. Re-capture every visual state of an element once to opt an existing setup into generic properties such as `TMP_Text.margin`.

When button support is enabled, `Click Behavior` controls whether a click leaves the view in `Active` (`Stay Active`) or returns it to `Idle` (`Return To Idle`).
