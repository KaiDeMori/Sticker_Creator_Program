/**
 * The JS↔C# message bridge.
 * Each request awaits exactly one reply, and only one call may be in flight at a time — see SCP_Main.cs's switch, where every request branch replies exactly once.
 * Navigations (open_pack, open_pack_selection) reply zero times and tear the page down, so they use notify(), not send().
 *
 * A long-running operation that streams several messages before it finishes (conversion progress) does not fit the one-request-one-reply model: register a listener with on(type, handler) and those messages are delivered to it, bypassing the pending-reply channel.
 */

let pending = null;
const listeners = {};

/**
 * A second call before the first has replied would silently strand the first promise.
 * By design this never happens; make it loud rather than mysterious.
 */
function send(type, payload) {
  if (pending) {
    throw new Error("message_bridge: a bridge call is already pending — refusing to send '" + type + "'");
  }
  return new Promise((resolve, reject) => {
    pending = { resolve, reject };
    window.external.sendMessage(JSON.stringify({ type, payload }));
  });
}

function notify(type, payload) {
  window.external.sendMessage(JSON.stringify({ type, payload }));
}

function on(type, handler) {
  listeners[type] = handler;
}

window.external.receiveMessage(function (raw) {
  const message = JSON.parse(raw);

  const listener = listeners[message.type];
  if (listener) {
    listener(message);
    return;
  }

  if (!pending) {
    // A reply with nothing awaiting it: the one legitimate cause is a fire-and-forget navigation whose C# handler threw before the page tore down.
    // No promise to reject, so surface it directly instead of dropping it silently.
    const detail = message.type === "error" ? message.payload : raw;
    window.alert("C# error:\n\n" + detail);
    console.error("bridge reply with no pending call:", message);
    return;
  }

  const { resolve, reject } = pending;
  pending = null;
  if (message.type === "error") {
    reject(new Error(message.payload));
  } else {
    resolve(message);
  }
});
