## Conventions

**This is a dark-canvas component set — always mount it on a dark page, never a light one.** Every component reads `--text`/`--text-dim`/`--border` for its own foreground, and those resolve to near-white values. There is no light theme and no theme switch. Set the page/frame background before placing any component:

```css
body { background: var(--bg); color: var(--text); }
```

`--bg` is pure black (`#000`). A `Modal` is the one exception — it paints its own opaque `--bg-card` panel, so it reads correctly even dropped onto a lighter canvas. Everything else (`Button` ghost variant, `Chip`, `EmptyState`, `Pagination`'s status text) will render invisible-on-white if you skip this.

No provider, no context, no hooks — every component is a plain function of props. Import styles once via `acordanza-design-system/styles.css`; nothing else to wire up.

### Styling idiom: CSS custom properties, not utility classes

Components carry their own built-in styling (internal class names like `.primary-btn` are an implementation detail, not part of the API — never reference them). You style your OWN layout glue around the components using this token set, so new UI matches:

| Token | Value | Use |
|---|---|---|
| `--bg` / `--bg-card` | `#000` / `#16181a` | page canvas / elevated card surface |
| `--text` / `--text-dim` | white / 72% white | primary / secondary foreground |
| `--accent` / `--accent-2` | `#494fdf` / `#00a87e` | brand violet / success teal |
| `--danger` | `#e23b4a` | destructive state |
| `--border` | 12% white | hairline dividers, ghost-button borders |
| `--radius-sm/md/lg/full` | 8 / 12 / 20 / 9999px | inputs·chips / cards / pills |
| `--space-xxs…xxxl` | 4 / 6 / 8 / 14 / 16 / 24 / 32 / 48px | the only spacing scale — never a raw px gap |
| `--font-display` / `--font-body` / `--font-mono` | Inter / Inter / JetBrains Mono | headings / body & UI text / grades |

`--font-display` and `--font-body` both resolve to Inter today — the type system doesn't yet split display from body weight, so don't invent a second display face. `--font-mono` is reserved for climbing grades (`GradePill`) — don't reuse it elsewhere.

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

`Button`'s default variant is a filled white pill (the loudest action — use once per view); `variant="ghost"` is the low-emphasis default for everything else, with `small` for compact placements and `danger` for destructive ghost actions.
