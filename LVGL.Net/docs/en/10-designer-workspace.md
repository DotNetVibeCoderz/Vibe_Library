# The designer workspace

One window: the board, a code editor over the same document, and the assistant docked on the
right.

```
┌─────────────────────────────────────────────────────────────────────────────────────────────────────────┐
│ LVGL.Net Designer  file.lvgl.json •  New Open Save Export · Undo Redo · [Design|Code] · 800×480 · Jack  │
├─ PARTS ───────┬─ board or code ─────────────────────────────────┬─ INSPECTOR ─────────┬─ JACK ──────────┤
│ PNL Panel     │                                                 │ [Properties|Styles] │ transcript      │
│ BTN Button    │      live LVGL preview on the board             │ BTN2 ActionButton   │                 │
│ ...           │                                                 │ PLACEMENT ...       │                 │
│ OUTLINE       │                                                 │ APPEARANCE ...      │                 │
│ LBL1 Title    │                                                 │ SHARED STYLES       │ composer        │
└───────────────┴─────────────────────────────────────────────────┴─────────────────────┴─────────────────┘
  status                                                 ● 2 things to check   ● LVGL 9.6.0   SNAP 8   100%
```

Every divider can be dragged. The assistant column appears when you open it and remembers its
width when you close and reopen it.

## Shortcuts

| Command | Shortcut |
|---|---|
| New / Open / Save / Save as | `Ctrl+N` / `Ctrl+O` / `Ctrl+S` / `Ctrl+Shift+S` |
| Export C# | `Ctrl+E` |
| Undo / Redo | `Ctrl+Z` / `Ctrl+Y` or `Ctrl+Shift+Z` |
| Duplicate / Delete / Deselect | `Ctrl+D` / `Del` / `Esc` |
| Nudge | Arrow keys, `Shift` for 10 px |
| Fit board to view | `Ctrl+0` |
| Show or hide the assistant | `Ctrl+J` |

On macOS, `Cmd` works wherever `Ctrl` is listed. While a text field or the code editor has focus,
`Ctrl+Z` and `Ctrl+D` belong to the text rather than the layout.

Undo covers everything that changes the document - property edits, drags, structure changes,
styles, applied JSON, layouts from the assistant. Typing a name is one step, not one per letter:
edits to the same field in quick succession are folded together.

## The inspector

The **Properties** page shows the fields that matter for the selected kind of widget - a range for
a slider, series colours for a chart, the target field for a keyboard - rather than one grid of
everything. With nothing selected it shows the screen's class name and background, and lists any
problems and warnings in the layout.

- **Number fields** take the arrow keys: Up/Down step by 1, with `Shift` by 10.
- **Colour fields** have a swatch that opens a picker beside the hex text. An empty field means the
  LVGL theme's colour.
- **Empty means default.** An empty optional field is "leave LVGL's default", not zero - the
  distinction the document format preserves.
- Fields edit as you type; an invalid value is outlined in red and not written to the document.

The **Styles** page edits shared styles: pick one, change it, and every widget using it follows.

## Design and Code modes

**Design** is the board with parts, outline and inspector. **Code** replaces the board with an
editor over three views of the same document:

| View | Editable | What it is |
|---|---|---|
| Layout JSON | Yes | The document itself. **Apply to design** (`Ctrl+Enter`) puts it on the canvas |
| Generated C# | No | The builder class, exactly as Export C# writes it |
| Event handlers | No | The hand-written half, with a stub per interactive widget |

![Code mode](../images/screenshots/designer-code.png)

The generated views are read-only on purpose: edits there would vanish on the next regeneration.
Applying JSON is an undoable step like any other; an invalid document is refused with the reason
in the editor's status line. The editor has line numbers, syntax highlighting, search (`Ctrl+F`)
and its own undo history.

## The assistant panel

**Jack** in the command bar, or `Ctrl+J`, opens the assistant described in
[chapter 9](09-assistant.md). When it produces a layout, the layout goes **straight onto the
board** - there is no button to press - and it is a normal undo step, so `Ctrl+Z` brings back what
you had. Ask, look, undo if it is not right, ask again.

![The assistant designing a screen](../images/screenshots/designer-assistant.png)

The assistant starts lazily: a session that never opens the panel never builds a model client,
never starts the attachment host and never reads an API key.
