# Message bridge

The only channel between the web pages and the C# host. Native, not HTTP: no server, no listening port, no `fetch`.

The complete message list is the switch in `SCP_Main.cs`. This document covers what that switch does not show.

## Transport properties

- Strings only, both directions. Every message is a JSON object with a `type` and a `payload`.
- No request identifier and no correlation of any kind.
- No page-load event on the C# side. The native window exists before the web view has parsed a document.
- `window.external.receiveMessage` is a registration function that installs a listener, not a property. Assigning a handler to it replaces the registration function before it runs and breaks delivery silently — no exception, no console output, and the host still reports successful sends.
- `SendWebMessage` marshals itself to the user-interface thread. Background work sends messages directly.

## Invariants

- **One request in flight per page.** Nothing correlates a reply to its request, so the next message received is the answer to the single outstanding request. A second concurrent request is refused.
- **Each handler answers exactly once**, or not at all for a navigation. This is a property of the handler code, not the transport. A second reply in any branch breaks every page.
- **A page's first message is its readiness signal.** The host never messages a page unprompted. Where the page also needs data, that data request is the readiness signal.
- **The host holds page state.** Which pack is open is C# state. No editor message names a pack. The two messages carrying a pack name are sent from the pack list, where no pack is open.
- **Streamed work bypasses the request slot.** Operations reporting more than once register a listener per message type. They are started fire-and-forget, never awaited.
- **An operation reading pack state from disk must first flush the page's pending edits.** The Editor autosaves through a queue, and a fire-and-forget message would overtake it and be answered from a stale file. The publish path awaits that queue before it may issue its request; the one-request-in-flight rule then enforces the ordering, because a request cannot be sent while a save is outstanding.
- **Modal native dialogs must not open from inside the callback.** A native dialog spins a nested message loop and would re-enter the callback's stack. The folder dialog is opened asynchronously and replies from the continuation.

## Failure paths

- **Expected failures** are part of an operation's result. They reply with the operation's own message type carrying an `ok` flag.
- **Unexpected failures** produce an `error` message, which rejects the pending request at the call site that caused it.

## Images bypass the bridge

Sticker images and static assets are absolute `file://` URLs resolved by the browser, with no host involvement per image.

A page loaded from a real `file://` URL may reference any absolute `file://` URL from passive markup, not only paths beneath its own directory. Chromium restricts only script-driven `fetch` and XHR from `file://` pages.
