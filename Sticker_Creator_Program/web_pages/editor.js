let PACK = "";

const SHEET = "assets/emoji_catalog/sheet_64_dirty.png";
const SHEET_PX = 4092,
  CELL = 66,
  GLYPH = 64;
const PICKER_GLYPH_BASE = 26;

function toChar(unified) {
  return unified
    .split("-")
    .map((h) => String.fromCodePoint(parseInt(h, 16)))
    .join("");
}
let byChar;
function buildByCharMap() {
  byChar = new Map();
  for (const e of APPLE_EMOJI) {
    e.char = toChar(e.u);
    byChar.set(e.char, e);
    if (e.n) byChar.set(toChar(e.n), e);
  }
}
function resolve(emoji) {
  return byChar.get(emoji) || byChar.get(emoji + "️") || byChar.get(emoji.replace(/️/g, "")) || null;
}
function glyphStyle(e, size) {
  const s = size / GLYPH,
    bg = SHEET_PX * s;
  const x = -(e.x * CELL + 1) * s,
    y = -(e.y * CELL + 1) * s;
  return `background-image:url('${SHEET}');background-size:${bg}px ${bg}px;` + `background-position:${x}px ${y}px;width:${size}px;height:${size}px;`;
}

let state = [];
let meta = { title: "", author: "", cover: "" };
let errorList = [];
let activeCard = null;
let saveChain = Promise.resolve();

function queueSave() {
  saveChain = saveChain.then(() => send("save_pack", { meta, stickers: state })).then(applyErrorListFromReply);
  return saveChain;
}

/**
 * Three flagged states, judged on facts C# attaches on load: source_exists (a plain directory check) and probed width/height (null when the source couldn't be read at all).
 * "missing" doesn't block conversion — the conversion pipeline already builds its file list from a directory scan, so a missing source was never going to be touched by it.
 * "unreadable" and "not-square" both would fail a full-pack conversion attempt, so both keep blocking, same as today.
 */
function squareStatus(item) {
  if (!item.source_exists) return { state: "missing", blocksConvert: false, text: "source file missing" };
  if (item.width == null || item.height == null) return { state: "unreadable", blocksConvert: true, text: "source file unreadable" };
  if (item.width !== item.height) return { state: "not-square", blocksConvert: true, text: "not square" };
  return { state: "ok", blocksConvert: false, text: "" };
}

const grid = document.getElementById("grid");
const loading_overlay = document.getElementById("loading-overlay");
const loading_text = document.getElementById("loading-text");
const progress_label = document.getElementById("progress-label");
const progress_track = document.getElementById("progress-track");
const progress_fill = document.getElementById("progress-fill");
function showOverlay(text) {
  loading_text.textContent = text;
  loading_text.style.display = "";
  progress_label.style.display = "none";
  progress_track.style.display = "none";
  loading_overlay.style.display = "flex";
}
/**
 * During conversion the overlay shows only the "done/total" count and the bar — no per-file text, so it never reflows or jumps in width as filenames of varying length stream past.
 * Both counts are padded to Signal's own 200-sticker cap (3 digits) in a monospace font, so the digits themselves stay put as they change.
 */
function showProgress(done, total) {
  loading_text.style.display = "none";
  progress_track.style.display = "";
  progress_fill.style.width = `${total > 0 ? (done / total) * 100 : 0}%`;
  progress_label.textContent = total > 0 ? `${String(done).padStart(3)}/${String(total).padStart(3)}` : "";
  progress_label.style.display = "";
  loading_overlay.style.display = "flex";
}
function hideOverlay() {
  loading_overlay.style.display = "none";
}
function waitForImage(image) {
  if (image.complete) return Promise.resolve();
  return new Promise((resolve) => {
    image.addEventListener("load", resolve, { once: true });
    image.addEventListener("error", resolve, { once: true });
  });
}
function waitForImages(container) {
  return Promise.all([...container.querySelectorAll("img")].map(waitForImage));
}
function preloadImage(url) {
  const image = new Image();
  image.src = url;
  return waitForImage(image);
}
function defaultCoverFile() {
  const first = state.find((item) => item.converted);
  return first ? first.file : "";
}
function render() {
  grid.innerHTML = "";
  const default_cover_file = defaultCoverFile();
  state.forEach((item, i) => {
    const sq = squareStatus(item);
    const flagged = sq.state !== "ok";
    const flagClass = sq.state === "not-square" ? "not-square" : flagged ? "missing-source" : "";
    const card = document.createElement("div");
    card.className = flagClass ? `card ${flagClass}` : "card";
    card.draggable = !coverMode;
    card.dataset.i = i;
    const e = resolve(item.emoji);
    const slot = e ? `<span class="glyph" style="${glyphStyle(e, 36)}"></span>` : item.emoji ? `<span style="font-size:1.6em">${item.emoji}</span>` : `<span class="empty">＋</span>`;
    const slot_html = `<div class="emoji-slot" data-i="${i}" title="click to choose emoji">${slot}</div>`;
    const quality_text = item.converted && item.fit ? (item.fit === "lossless" ? "lossless" : `Q${item.quality}`) : "";
    const is_cover = item.converted && meta.cover && meta.cover === item.file;
    const is_default_cover = !meta.cover && item.file === default_cover_file;
    const cover_stacked = quality_text ? " stacked" : "";
    const coverBadge = is_cover ? `<span class="cover-badge${cover_stacked}">★ COVER</span>` : is_default_cover ? `<span class="cover-badge default${cover_stacked}">★ cover · first</span>` : "";
    const quality_html = quality_text ? `<div class="quality-badge">${quality_text}</div>` : "";
    const not_converted_badge = item.converted ? "" : `<span class="not-converted-badge">not converted</span>`;
    const cover_hint = item.converted ? `<div class="cover-hint">★ set as cover</div>` : "";
    const remove_button = sq.state === "missing" ? `<button class="remove-sticker-btn" data-i="${i}" draggable="false" title="Remove this sticker">🗑 Remove</button>` : "";
    const overlay_class = flagClass === "missing-source" ? "square-overlay missing-source" : "square-overlay";
    const square_overlay = flagged ? `<div class="${overlay_class}">${sq.text}${remove_button}</div>` : "";
    const image_source = item.url;
    card.innerHTML = slot_html + `<div class="thumb">` + `<img src="${image_source}" alt="" draggable="false">` + coverBadge + quality_html + not_converted_badge + cover_hint + square_overlay + `</div>` + `<div class="fname" title="${item.file}">${item.file}</div>`;
    card.addEventListener("click", () => {
      if (coverMode && item.converted) setCover(item.file);
    });
    grid.appendChild(card);
  });
  bindDnd();
  bindSlots();
  bindRemoveButtons();
  updateStat();
  updateCoverUI();
  updateConvertButton();
  updateValidityIndicator();
}
function updateStat() {
  document.getElementById("stat").textContent = `${state.length} stickers`;
}
const btnConvert = document.getElementById("btn-convert");
function updateConvertButton() {
  const anyBlocking = state.some((item) => squareStatus(item).blocksConvert);
  btnConvert.disabled = anyBlocking;
  btnConvert.classList.toggle("blocked", anyBlocking);
  btnConvert.title = anyBlocking ? "some stickers can't be converted — see the flagged cards" : "";
}

const btnValidity = document.getElementById("btn-validity");
function updateValidityIndicator() {
  const invalid = errorList.length > 0;
  btnValidity.textContent = invalid ? `⚠️ ${errorList.length} errors` : "✅ Valid";
  btnValidity.classList.toggle("blocked", invalid);
  btnPublish.disabled = invalid;
}

// Any edit — mapping an emoji, changing the cover, removing a sticker, … — can change pack validity.
// save_pack and remove_sticker both echo a freshly recomputed error_list so the Editor can pick up the change.
function applyErrorListFromReply(message) {
  errorList = message.payload.error_list;
  updateValidityIndicator();
  if (validityDlg.open) renderValidityDialog();
}

function bindSlots() {
  grid.querySelectorAll(".emoji-slot").forEach((el) => el.addEventListener("click", () => openPicker(+el.dataset.i)));
}

// stopPropagation is load-bearing: the card-level click handler fires setCover(item.file) whenever coverMode && item.converted — and a previously-converted sticker whose source later went missing still has item.converted === true (conversion status tracks the .webp stem on disk, independent of the source), so an unguarded click here would also flip the cover while removing.
function bindRemoveButtons() {
  grid.querySelectorAll(".remove-sticker-btn").forEach((el) =>
    el.addEventListener("click", (e) => {
      e.stopPropagation();
      requestRemoval(+el.dataset.i);
    })
  );
}
const picker = document.getElementById("picker");
const pickerBody = document.getElementById("picker-body");
const search = document.getElementById("search");
let pickerBuilt = false;
let picker_zoom = 1;
function buildPicker() {
  const cats = {};
  for (const e of APPLE_EMOJI) {
    (cats[e.c] = cats[e.c] || []).push(e);
  }
  const order = ["Smileys & Emotion", "People & Body", "Animals & Nature", "Food & Drink", "Activities", "Travel & Places", "Objects", "Symbols", "Flags", "Component"];
  const keys = Object.keys(cats).sort((a, b) => {
    const ia = order.indexOf(a),
      ib = order.indexOf(b);
    return (ia < 0 ? 99 : ia) - (ib < 0 ? 99 : ib);
  });
  let html = "";
  for (const k of keys) {
    html += `<div class="cat-head" data-cat="${k}">${k}</div><div class="emoji-row" data-cat="${k}">`;
    for (const e of cats[k]) {
      const hay = (e.name + " " + e.s.join(" ")).toLowerCase();
      html += `<button class="pick" data-char="${e.char}" data-hay="${hay}" title="${e.s[0] || e.name}">` + `<span class="glyph" style="${glyphStyle(e, PICKER_GLYPH_BASE * picker_zoom)}"></span></button>`;
    }
    html += `</div>`;
  }
  pickerBody.innerHTML = html;
  pickerBody.querySelectorAll(".pick").forEach((b) => b.addEventListener("click", () => choose(b.dataset.char)));
  pickerBuilt = true;
}
function openPicker(i) {
  if (!pickerBuilt) buildPicker();
  activeCard = i;
  search.value = "";
  filterPicker("");
  picker.showModal();
  search.focus();
}
function choose(emoji) {
  if (activeCard != null) {
    state[activeCard].emoji = emoji;
    queueSave();
    render();
  }
  picker.close();
}
function filterPicker(q) {
  q = q.trim().toLowerCase();
  pickerBody.querySelectorAll(".pick").forEach((b) => (b.style.display = !q || b.dataset.hay.includes(q) ? "" : "none"));
  pickerBody.querySelectorAll(".emoji-row").forEach((row) => {
    const any = [...row.querySelectorAll(".pick")].some((b) => b.style.display !== "none");
    row.style.display = any ? "" : "none";
    row.previousElementSibling.style.display = any ? "" : "none";
  });
}
search.addEventListener("input", () => filterPicker(search.value));
document.getElementById("picker-close").addEventListener("click", () => picker.close());

const zoomSlider = document.getElementById("zoom");
const zoomValue = document.getElementById("zoom-value");
/** Restyles the glyphs already in the picker; a picker not yet built instead renders at picker_zoom once it is. */
function applyPickerZoom(zoom) {
  picker_zoom = zoom;
  zoomValue.textContent = zoom.toFixed(1) + "×";
  const size = PICKER_GLYPH_BASE * zoom;
  pickerBody.querySelectorAll(".pick").forEach((b) => {
    const e = byChar.get(b.dataset.char);
    if (e) b.querySelector(".glyph").setAttribute("style", glyphStyle(e, size));
  });
}
zoomSlider.addEventListener("input", () => applyPickerZoom(parseFloat(zoomSlider.value)));
// Persisting on "change" rather than "input" writes the config once per drag instead of once per pixel.
zoomSlider.addEventListener("change", () => notify("set_picker_zoom", picker_zoom));

let dragIndex = null;
function bindDnd() {
  grid.querySelectorAll(".card").forEach((card) => {
    card.addEventListener("dragstart", (e) => {
      dragIndex = +card.dataset.i;
      card.classList.add("dragging");
    });
    card.addEventListener("dragend", (e) => card.classList.remove("dragging"));
    card.addEventListener("dragover", (e) => {
      e.preventDefault();
      card.classList.add("over");
    });
    card.addEventListener("dragleave", (e) => card.classList.remove("over"));
    card.addEventListener("drop", (e) => {
      e.preventDefault();
      card.classList.remove("over");
      const to = +card.dataset.i;
      if (dragIndex == null || dragIndex === to) return;
      const [moved] = state.splice(dragIndex, 1);
      state.splice(to, 0, moved);
      dragIndex = null;
      queueSave();
      render();
    });
  });
}

const elTitle = document.getElementById("meta-title");
const elAuthor = document.getElementById("meta-author");
const btnCover = document.getElementById("btn-cover");
const coverLabel = document.getElementById("cover-label");
function updateDocumentTitle() {
  document.title = `Sticker Creator Program — Editor — ${meta.title || PACK}`;
}
elTitle.addEventListener("input", () => {
  meta.title = elTitle.value;
  updateDocumentTitle();
  queueSave();
});
elAuthor.addEventListener("input", () => {
  meta.author = elAuthor.value;
  queueSave();
});

let coverMode = false;
function updateCoverUI() {
  btnCover.classList.toggle("active", coverMode);
  btnCover.textContent = coverMode ? "Selecting cover… (Esc to cancel)" : "🎯 Set cover";
  const fallback_file = defaultCoverFile();
  coverLabel.textContent = meta.cover ? `cover: ${meta.cover}` : fallback_file ? `cover: ${fallback_file} (first converted)` : "cover: not set";
}
function enterCoverMode() {
  coverMode = true;
  grid.classList.add("cover-mode");
  render();
}
function exitCoverMode() {
  coverMode = false;
  grid.classList.remove("cover-mode");
  render();
}
function setCover(file) {
  meta.cover = meta.cover === file ? "" : file;
  queueSave();
  exitCoverMode();
}
btnCover.addEventListener("click", () => (coverMode ? exitCoverMode() : enterCoverMode()));

let pendingRemoveIndex = null;
const removeConfirmDlg = document.getElementById("remove-confirm");
const removeConfirmText = document.getElementById("remove-confirm-text");
function requestRemoval(index) {
  const item = state[index];
  if (item.emoji) {
    pendingRemoveIndex = index;
    removeConfirmText.textContent = `"${item.file}" is mapped to ${item.emoji} — removing it loses that mapping.`;
    removeConfirmDlg.showModal();
  } else {
    removeSticker(index);
  }
}
document.getElementById("remove-confirm-cancel").addEventListener("click", () => removeConfirmDlg.close());
document.getElementById("remove-confirm-ok").addEventListener("click", () => {
  removeConfirmDlg.close();
  if (pendingRemoveIndex != null) removeSticker(pendingRemoveIndex);
  pendingRemoveIndex = null;
});
function removeSticker(index) {
  const file = state[index].file;
  state.splice(index, 1); // splice, not filter(file) — duplicate `file` values can survive reconcile, and a filter would remove every copy at once
  if (meta.cover === file) meta.cover = "";
  saveChain = saveChain
    .then(() => send("remove_sticker", { file, meta, stickers: state }))
    .then(applyErrorListFromReply)
    .catch((error) => {
      console.error(error);
      alert("Removing the sticker failed:\n\n" + error.message);
    });
  render();
  return saveChain;
}
document.addEventListener("keydown", (e) => {
  if (e.key === "Escape" && coverMode) exitCoverMode();
});

// Conversion runs on a C# background thread and streams progress, so it is fired and forgotten here; convert_progress updates the overlay and convert_result ends it.
btnConvert.addEventListener("click", () => {
  showProgress(0, 0);
  notify("convert_pack");
});
const chkLossless = document.getElementById("chk-lossless");
let lossless_warning_was_shown = false;
const losslessWarningDlg = document.getElementById("lossless-warning");
document.getElementById("lossless-warning-ok").addEventListener("click", () => losslessWarningDlg.close());
chkLossless.addEventListener("change", () => {
  if (chkLossless.checked && !lossless_warning_was_shown) {
    lossless_warning_was_shown = true;
    notify("set_lossless_warning_shown");
    losslessWarningDlg.showModal();
  }
  notify("set_lossless_enabled", chkLossless.checked);
});
on("convert_progress", (message) => {
  const p = message.payload;
  console.log(`convert ${p.done}/${p.total}  ${p.file} → ${p.quality != null ? `Q${p.quality}` : p.fit}`);
  showProgress(p.done, p.total);
});
on("convert_result", (message) => {
  const payload = message.payload;
  if (!payload.ok) {
    hideOverlay();
    const lines = payload.problems.map((p) =>
      p.error ? `${p.file}: ${p.error}` : `${p.file}: ${p.width}×${p.height} (not square)`
    );
    alert("Conversion failed:\n\n" + lines.join("\n"));
    return;
  }
  if (payload.trophy) {
    // Left showing through the navigation so the switch to the trophy page is seamless.
    notify("open_trophy");
    return;
  }
  // Left showing through the reload so the switch to the fresh page's overlay is seamless.
  location.reload();
});

// Routes through the pending save chain first, so the mapping is flushed to disk before the page leaves the Editor.
document.getElementById("btn-back").addEventListener("click", () => {
  saveChain.then(() => {
    notify("open_pack_selection");
  });
});

// Same save-chain-first shape as btn-back: no confirmation needed since every edit already autosaves.
// Reuses the exact reload path convert_result already relies on, which re-runs this page's own get_pack bootstrap.
document.getElementById("btn-refresh").addEventListener("click", () => {
  saveChain.then(() => location.reload());
});

function buildYaml() {
  const q = (s) => (/[:#]/.test(s) ? JSON.stringify(s) : s);
  const lines = ["meta:", `  author: ${q(meta.author)}`];
  if (meta.cover) lines.push(`  cover: ${q(meta.cover)}`);
  lines.push(`  title: ${q(meta.title)}`, "stickers:");
  for (const s of state) {
    lines.push(`- emoji: ${s.emoji}`, `  file: ${q(s.file)}`);
  }
  return lines.join("\n") + "\n";
}
const exportDlg = document.getElementById("export");
document.getElementById("btn-export").addEventListener("click", () => {
  document.getElementById("export-text").value = buildYaml();
  exportDlg.showModal();
});
document.getElementById("export-close").addEventListener("click", () => exportDlg.close());
document.getElementById("export-copy").addEventListener("click", () => {
  const t = document.getElementById("export-text");
  t.select();
  document.execCommand("copy");
});

const validityDlg = document.getElementById("validity-dialog");
const validityList = document.getElementById("validity-list");
const btnPublish = document.getElementById("btn-publish");
function renderValidityDialog() {
  validityList.innerHTML = errorList.length
    ? errorList.map((e) => `<li>${e.file ? `<code>${e.file}</code> — ` : ""}${e.condition}</li>`).join("")
    : "<li>No errors — ready to publish.</li>";
}
btnValidity.addEventListener("click", () => {
  renderValidityDialog();
  validityDlg.showModal();
});
document.getElementById("validity-close").addEventListener("click", () => validityDlg.close());

const publishConfirmDlg = document.getElementById("publish-confirm");
const publishResultDlg = document.getElementById("publish-result");
const publishResultText = document.getElementById("publish-result-text");

btnPublish.addEventListener("click", () => {
  validityDlg.close();
  publishConfirmDlg.showModal();
});
document.getElementById("publish-confirm-cancel").addEventListener("click", () => publishConfirmDlg.close());
document.getElementById("publish-confirm-ok").addEventListener("click", () => {
  publishConfirmDlg.close();
  showOverlay("Publishing…");
  notify("publish_pack");
});
document.getElementById("publish-result-close").addEventListener("click", () => publishResultDlg.close());
// publish_pack is fired and forgotten — the C# handler streams back exactly one publish_result once the background upload finishes, same shape convert_pack already uses for convert_result.
on("publish_result", (message) => {
  hideOverlay();
  const payload = message.payload;
  if (payload.ok) {
    artUrl = payload.url;
    btnInstall.style.display = "";
    openInstallDialog(artUrl);
  } else {
    publishResultText.textContent = payload.error;
    publishResultDlg.showModal();
  }
});

const btnInstall = document.getElementById("btn-install");
const installPackDlg = document.getElementById("install-pack");
const installPackUrlInput = document.getElementById("install-pack-url");
const installPackQr = document.getElementById("install-pack-qr");
let artUrl = "";

/**
 * The QR code is rendered server-side on demand rather than carried on every get_pack reply — the dialog is opened far less often than the pack itself.
 */
function openInstallDialog(url) {
  installPackUrlInput.value = url;
  installPackQr.removeAttribute("src");
  installPackDlg.showModal();
  send("get_install_qr").then((message) => {
    if (message.payload.ok) installPackQr.src = message.payload.qr_data_url;
  });
}
btnInstall.addEventListener("click", () => openInstallDialog(artUrl));
document.getElementById("install-pack-close").addEventListener("click", () => installPackDlg.close());
document.getElementById("install-pack-copy").addEventListener("click", () => {
  installPackUrlInput.select();
  document.execCommand("copy");
});
document.getElementById("install-pack-open").addEventListener("click", () => {
  if (artUrl) notify("open_external_url", artUrl);
});

const installPackNoteToSelfBtn = document.getElementById("install-pack-note-to-self");
installPackNoteToSelfBtn.addEventListener("click", () => {
  if (!artUrl) return;
  console.log("send_note_to_self");
  installPackNoteToSelfBtn.disabled = true;
  notify("send_note_to_self");
});
// send_note_to_self is fired and forgotten — the C# handler streams back exactly one note_to_self_result once the background signal-cli call finishes, same shape publish_pack uses for publish_result.
on("note_to_self_result", (message) => {
  installPackNoteToSelfBtn.disabled = false;
  const payload = message.payload;
  if (payload.ok) console.log("note_to_self_result ok");
  else console.error("note_to_self_result", payload.error);
});

buildByCharMap();
send("get_pack")
  .then((message) => {
    PACK = message.payload.pack;
    state = message.payload.stickers;
    meta = message.payload.meta;
    artUrl = message.payload.art_url;
    btnInstall.style.display = artUrl ? "" : "none";
    errorList = message.payload.error_list;
    chkLossless.checked = message.payload.enable_lossless_compression;
    lossless_warning_was_shown = message.payload.lossless_warning_was_shown;
    // Reading the value back off the slider clamps a stored zoom that falls outside the control's range into it.
    zoomSlider.value = message.payload.picker_zoom;
    applyPickerZoom(parseFloat(zoomSlider.value));
    elTitle.value = meta.title;
    elAuthor.value = meta.author;
    updateDocumentTitle();
    render();
    return Promise.all([waitForImages(grid), preloadImage(SHEET)]);
  })
  .then(hideOverlay)
  .catch((error) => {
    showOverlay("C# error — see console (F12)");
    console.error(error);
  });
