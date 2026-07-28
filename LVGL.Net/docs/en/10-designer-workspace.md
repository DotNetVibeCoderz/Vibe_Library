# The designer workspace

The designer is one window with three movable parts: the visual canvas, a code editor over the
same document, and the assistant docked on the right.

```
┌───────────────────────────────────────────────────────────────────────┬──────────────┐
│ New Open Save Export C# [Design|Code] [Toolbox][Inspector] 800 x 480  │  Ask Jack ▣  │
├─────────┬───────────────────────────────────────┬─────────────────────┼──────────────┤
│         │                                       │  OUTLINE            │              │
│ TOOLBOX │        canvas  or  code editor        ├─────────────────────┤   assistant  │
│         │                                       │  PROPERTIES         │              │
├─────────┴───────────────────────────────────────┴─────────────────────┴──────────────┤
│ status                                                        [Undo assistant change] │
└───────────────────────────────────────────────────────────────────────────────────────┘
```

## Panels

All three side panels hide and show the same way, and all three are resizable by dragging their
divider. Each remembers the width you dragged it to, so reopening restores your layout rather than
snapping back to a default.

| Panel | Toggle | Shortcut |
|---|---|---|
| Toolbox (left) | **Toolbox** in the toolbar, or the ✕ in its header | `Ctrl+B` |
| Outline and properties (right) | **Inspector**, or the ✕ in its header | `Ctrl+Shift+B` |
| Assistant (far right) | **Ask Jack**, or the ✕ in its header | `Ctrl+J` |

On a small screen, hiding the toolbox and inspector gives the canvas most of the window while
keeping the assistant open beside it.

Code mode hides the toolbox and inspector automatically — they act on the canvas, so there is
nothing for them to do — and disables their toggles while it does. Your choice is remembered and
comes back when you return to Design.

## Design and Code modes

The **Design | Code** switch in the toolbar changes what the centre pane shows. Both work on the
same document, so switching does not lose anything.

**Design** is the visual canvas with the toolbox and inspector either side.

**Code** replaces the canvas with an editor. A dropdown chooses what to look at:

| View | Editable | What it is |
|---|---|---|
| Layout (JSON) | Yes | The document itself. **Apply to design** parses it back onto the canvas |
| Generated C# | No | The builder class, exactly as Export C# would write it |
| Event handlers | No | The hand-written half, with a stub per interactive widget |

The two generated views are read-only on purpose: edits there would be silently discarded the next
time they regenerate, which is worse than not offering it. Layout JSON is the one editable view,
and it closes the loop — hand-edit the document, apply, and see the result on the canvas.

An invalid edit is refused with the reason in the status strip rather than being applied.

## The editor

| Command | Shortcut |
|---|---|
| Undo / Redo | `Ctrl+Z` / `Ctrl+Y` |
| Cut / Copy / Paste | `Ctrl+X` / `Ctrl+C` / `Ctrl+V` |
| Select all | `Ctrl+A` |
| Find and replace | `Ctrl+F`, `F3` for next |
| Go to line | `Ctrl+G` |
| Apply to design | `Ctrl+Enter` |

All of it is also on the toolbar and in the right-click menu, so nothing is keyboard-only.

**Line numbers** and **Wrap** are toggles in the toolbar. The status strip shows the caret position
and the selection length.

Syntax highlighting follows the view — JSON for the layout, C# for the generated code.

## The assistant panel

**Ask Jack** in the toolbar, or `Ctrl+J`, shows and hides the assistant. Drag the divider to resize
it; the width is remembered when you close and reopen it. The **✕** in the panel header hides it too.

The panel is the same assistant described in [chapter 9](09-assistant.md) — sessions, providers,
attachments and tools are unchanged. What differs from a separate window is what happens when it
produces a layout:

**Layouts are applied to the canvas immediately.** There is no button to press. Ask for a screen,
watch it appear. If you were in Code mode, the editor updates too.

That is safe because of the **Undo assistant change** button that appears in the status bar. It
restores the document from before the change, one step. Applying is not destructive, so the loop
stays fast — ask, look, revert if it is not what you wanted, ask again.

Warnings on an applied layout (a widget off screen, an alignment offset that looks like an absolute
coordinate) are shown in the status bar rather than blocking the apply.

The assistant starts lazily: a designer session that never opens the panel never builds a kernel,
never starts the attachment host, and never reads the API keys.
