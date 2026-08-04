## Conventions

**This is a light, "Cool Minimal" component set — mount it on a light page.** `--bg` is a cool light-gray canvas, `--text` is near-black; every component's own foreground/background already resolves correctly against that pairing. There is no dark theme and no theme switch. Set the page/frame background before placing any component:

```css
body { background: var(--bg); color: var(--text); }
```

`--bg` is `#eef1f5`. `Modal` paints its own opaque `--bg-card` panel (near-white, `#f8f9fb`), so it reads correctly even dropped onto a different canvas.

No provider, no context, no hooks — every component is a plain function of props. Import styles once via `acordanza-design-system/styles.css`; nothing else to wire up.

### Styling idiom: CSS custom properties, not utility classes

Components carry their own built-in styling (internal class names like `.primary-btn` are an implementation detail, not part of the API — never reference them). You style your OWN layout glue around the components using this token set, so new UI matches:

| Token | Value | Use |
|---|---|---|
| `--bg` / `--bg-card` | `#eef1f5` / `#f8f9fb` | page canvas / elevated card surface |
| `--bg-card-hover` / `--bg-input` | `#eef0f4` / `#ffffff` | hovered card / input fields |
| `--text` / `--text-dim` / `--dim` | `#181b22` / `#5b6270` / `#7d8492` | primary / secondary / faint foreground |
| `--accent` / `--on-accent` | `#3f3fe0` / `#ffffff` | brand violet-blue (loudest action) / text on it |
| `--accent-2` | `#007a63` | success teal — used by `GradePill` |
| `--danger` | `#d13a4a` | destructive state |
| `--border` / `--border-strong` | 7% black / 15% black | hairline dividers, ghost-button borders |
| `--radius-sm/md/lg/full` | 8 / 12 / 20 / 9999px | inputs·chips / cards / pills |
| `--space-xxs…xxxl` | 4 / 6 / 8 / 14 / 16 / 24 / 32 / 48px | the only spacing scale — never a raw px gap |
| `--font-display` / `--font-body` / `--font-mono` | Archivo / Inter / JetBrains Mono | headings / body & UI text / grades |

`--font-display` (Archivo, weights 500–800) is used for `Modal`'s `h2`/`h3` only — everything else reads `--font-body` (Inter). `--font-mono` is reserved for climbing grades (`GradePill`) — don't reuse it elsewhere.

### Where the truth lives

Read `styles.css` (root tokens) and `_ds_bundle.css` (per-component rules) before styling anything by hand — both ship in this bundle. Each component's `<Name>.d.ts` is its full prop contract; `Button`'s `as` prop makes it polymorphic (e.g. `as="a"` for a link-styled action) while keeping its variant styling.

### Composition example

The canonical composition is a form inside a `Modal`, actions right-aligned via `ModalActions`:

```tsx
<Modal onClose={onClose}>
  <h2>Registrar ascenso</h2>
  <form onSubmit={handleSubmit}>
    {/* inputs — plain <input>, unstyled by this package */}
    <ModalActions>
      <Button variant="ghost" type="button" onClick={onClose}>Cancelar</Button>
      <Button type="submit">Registrar</Button>
    </ModalActions>
  </form>
</Modal>
```

`Button`'s default variant is a filled violet-blue pill (the loudest action — use once per view); `variant="ghost"` is the low-emphasis default for everything else, with `small` for compact placements and `danger` for destructive ghost actions.
