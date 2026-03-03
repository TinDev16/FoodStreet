const form = document.getElementById("poiForm");
const rows = document.getElementById("poiRows");
const statusText = document.getElementById("status");
const resetBtn = document.getElementById("resetBtn");
const gpsInput = form.elements.gps;
const latPreview = document.getElementById("latPreview");
const lonPreview = document.getElementById("lonPreview");

const audioFileInput = form.elements.audioFile;
const ttsInput = form.elements.ttsText;
const imageFileInput = form.elements.imageFile;

const existingMedia = document.getElementById("existingMedia");
const currentAudioLink = document.getElementById("currentAudioLink");
const currentAudioEmpty = document.getElementById("currentAudioEmpty");
const currentTtsText = document.getElementById("currentTtsText");
const currentImageLink = document.getElementById("currentImageLink");
const currentImageEmpty = document.getElementById("currentImageEmpty");
const clearAudioBtn = document.getElementById("clearAudioBtn");
const clearTtsBtn = document.getElementById("clearTtsBtn");
const clearImageBtn = document.getElementById("clearImageBtn");

const state = {
  currentAudioUrl: "",
  currentTtsText: "",
  currentImageUrl: "",
  clearAudio: false,
  clearTts: false,
  clearImage: false
};

gpsInput.addEventListener("input", updateGpsPreview);
resetBtn.addEventListener("click", () => resetForm(true));
form.addEventListener("submit", onSubmit);
audioFileInput?.addEventListener("change", updateMediaConstraints);
ttsInput?.addEventListener("input", updateMediaConstraints);
imageFileInput?.addEventListener("change", updateImageInputConstraints);
clearAudioBtn?.addEventListener("click", onClearAudioClicked);
clearTtsBtn?.addEventListener("click", onClearTtsClicked);
clearImageBtn?.addEventListener("click", onClearImageClicked);

loadPois();
resetForm(false);

async function loadPois() {
  try {
    const response = await fetch("/api/shops", { cache: "no-store" });
    if (!response.ok) {
      setStatus(`Khong tai duoc danh sach POI (${response.status}).`, true);
      return;
    }

    const payload = await response.json();
    const list = Array.isArray(payload) ? payload : (Array.isArray(payload?.value) ? payload.value : []);
    rows.innerHTML = "";

    for (const item of list) {
      const tr = document.createElement("tr");
      const audioLabel = item.audioUrl ? "Audio file" : (item.ttsText ? "TTS text" : "Khong");
      const imageLabel = item.imageUrl ? `<a href="${escapeHtml(item.imageUrl)}" target="_blank" rel="noopener">Xem</a>` : "Khong";
      tr.innerHTML = `
      <td>${escapeHtml(item.shopName)}</td>
      <td>${item.latitude}, ${item.longitude}</td>
      <td>${item.radiusMeters}</td>
      <td>${imageLabel}</td>
      <td>${audioLabel}</td>
      <td>
        <button type="button" data-action="edit" data-id="${item.id}">Sua</button>
        <button type="button" data-action="delete" data-id="${item.id}" class="danger">Xoa</button>
      </td>
      `;
      rows.appendChild(tr);
    }

    rows.querySelectorAll("button[data-action='edit']").forEach(btn =>
      btn.addEventListener("click", () => editPoi(btn.dataset.id)));
    rows.querySelectorAll("button[data-action='delete']").forEach(btn =>
      btn.addEventListener("click", () => deletePoi(btn.dataset.id)));

    if (list.length === 0) {
      setStatus("Danh sach POI dang rong.");
    }
  } catch (error) {
    setStatus(`Khong tai duoc danh sach POI: ${error}`, true);
  }
}

async function editPoi(id) {
  const response = await fetch(`/api/shops/${id}`);
  if (!response.ok) {
    setStatus("Khong tai duoc POI.", true);
    return;
  }

  const item = await response.json();
  form.elements.id.value = item.id;
  form.elements.shopName.value = item.shopName || "";
  form.elements.gps.value = `${item.latitude}, ${item.longitude}`;
  form.elements.radiusMeters.value = item.radiusMeters;
  form.elements.description.value = item.description || "";
  form.elements.ttsText.value = "";
  form.elements.audioFile.value = "";
  form.elements.imageFile.value = "";

  state.currentAudioUrl = item.audioUrl || "";
  state.currentTtsText = item.ttsText || "";
  state.currentImageUrl = item.imageUrl || "";
  state.clearAudio = false;
  state.clearTts = false;
  state.clearImage = false;

  updateGpsPreview();
  updateMediaConstraints();
  updateImageInputConstraints();
  renderExistingMedia();
  if (state.currentAudioUrl) {
    setStatus(`Da nap audio cu: ${state.currentAudioUrl}`);
  }
}

async function deletePoi(id) {
  if (!confirm(`Xoa POI ${id}?`)) {
    return;
  }

  const response = await fetch(`/api/shops/${id}`, { method: "DELETE" });
  if (!response.ok) {
    setStatus("Xoa that bai.", true);
    return;
  }

  setStatus("Da xoa.");
  if (form.elements.id.value === id) {
    resetForm(false);
  }
  await loadPois();
}

async function onSubmit(event) {
  event.preventDefault();
  const gps = parseGps(form.elements.gps.value);
  if (!gps) {
    setStatus("GPS khong hop le. Dung dinh dang: lat, lon", true);
    return;
  }

  if (!validateAudioTtsMutualExclusion()) {
    return;
  }

  const body = new FormData();
  if (form.elements.id.value.trim()) {
    body.append("id", form.elements.id.value.trim());
  }

  body.append("shopName", form.elements.shopName.value.trim());
  body.append("gps", form.elements.gps.value.trim());
  body.append("radiusMeters", form.elements.radiusMeters.value);
  body.append("description", form.elements.description.value.trim());
  body.append("ttsText", ttsInput.value.trim());
  body.append("clearAudio", state.clearAudio ? "1" : "0");
  body.append("clearTts", state.clearTts ? "1" : "0");
  body.append("clearImage", state.clearImage ? "1" : "0");

  const audioFile = audioFileInput.files[0];
  if (audioFile) {
    body.append("audioFile", audioFile);
  }

  const imageFile = imageFileInput?.files?.[0];
  if (imageFile) {
    body.append("imageFile", imageFile);
  }

  const response = await fetch("/api/shops", { method: "POST", body });
  if (!response.ok) {
    const err = await safeReadError(response);
    setStatus(`Luu that bai: ${err}`, true);
    return;
  }

  setStatus("Luu thanh cong.");
  resetForm(false);
  await loadPois();
}

function onClearAudioClicked() {
  if (!state.currentAudioUrl) {
    return;
  }

  state.currentAudioUrl = "";
  state.clearAudio = true;
  audioFileInput.value = "";
  updateMediaConstraints();
  renderExistingMedia();
}

function onClearTtsClicked() {
  if (!state.currentTtsText && !ttsInput.value.trim()) {
    return;
  }

  state.currentTtsText = "";
  state.clearTts = true;
  ttsInput.value = "";
  updateMediaConstraints();
  renderExistingMedia();
}

function onClearImageClicked() {
  if (!state.currentImageUrl) {
    return;
  }

  state.currentImageUrl = "";
  state.clearImage = true;
  if (imageFileInput) {
    imageFileInput.value = "";
  }
  updateImageInputConstraints();
  renderExistingMedia();
}

function validateAudioTtsMutualExclusion() {
  const hasExistingAudio = !!state.currentAudioUrl;
  const hasExistingTts = !!state.currentTtsText;
  const hasNewAudio = !!audioFileInput.files[0];
  const hasNewTts = !!ttsInput.value.trim();

  if ((hasExistingAudio || hasNewAudio) && hasNewTts) {
    setStatus("Audio file va TTS chi duoc chon 1. Neu muon doi kieu, hay xoa ben con lai.", true);
    return false;
  }

  if ((hasExistingTts || hasNewTts) && hasNewAudio) {
    setStatus("Audio file va TTS chi duoc chon 1. Neu muon doi kieu, hay xoa ben con lai.", true);
    return false;
  }

  return true;
}

function parseGps(raw) {
  if (!raw) return null;
  const parts = raw.split(",").map(x => x.trim());
  if (parts.length !== 2) return null;
  const lat = Number(parts[0]);
  const lon = Number(parts[1]);
  if (!Number.isFinite(lat) || !Number.isFinite(lon)) return null;
  if (lat < -90 || lat > 90 || lon < -180 || lon > 180) return null;
  return { lat, lon };
}

function updateGpsPreview() {
  const gps = parseGps(gpsInput.value);
  latPreview.textContent = gps ? String(gps.lat) : "--";
  lonPreview.textContent = gps ? String(gps.lon) : "--";
}

function updateMediaConstraints() {
  const hasAudio = !!state.currentAudioUrl || !!audioFileInput.files[0];
  const hasTts = !!state.currentTtsText || !!ttsInput.value.trim();

  if (hasAudio) {
    ttsInput.value = "";
    ttsInput.disabled = true;
    clearTtsBtn.disabled = !state.currentTtsText;
  } else {
    ttsInput.disabled = false;
    clearTtsBtn.disabled = !state.currentTtsText;
  }

  if (hasTts) {
    audioFileInput.value = "";
    audioFileInput.disabled = true;
    clearAudioBtn.disabled = !state.currentAudioUrl;
  } else {
    audioFileInput.disabled = false;
    clearAudioBtn.disabled = !state.currentAudioUrl;
  }

  renderExistingMedia();
}

function updateImageInputConstraints() {
  const hasCurrentImage = !!state.currentImageUrl;
  if (imageFileInput) {
    imageFileInput.disabled = false;
  }
  clearImageBtn.disabled = !hasCurrentImage;
}

function renderExistingMedia() {
  existingMedia.style.display = "block";

  const hasAudio = !!state.currentAudioUrl;
  currentAudioLink.hidden = !hasAudio;
  currentAudioEmpty.hidden = hasAudio;
  if (hasAudio) {
    currentAudioLink.href = state.currentAudioUrl;
    currentAudioLink.textContent = state.currentAudioUrl;
  } else {
    currentAudioLink.href = "#";
    currentAudioLink.textContent = "";
  }

  currentTtsText.textContent = state.currentTtsText || "Khong co";

  const hasImage = !!state.currentImageUrl;
  currentImageLink.hidden = !hasImage;
  currentImageEmpty.hidden = hasImage;
  if (hasImage) {
    currentImageLink.href = state.currentImageUrl;
    currentImageLink.textContent = state.currentImageUrl;
  } else {
    currentImageLink.href = "#";
    currentImageLink.textContent = "";
  }
}

function resetForm(clearStatus) {
  form.reset();
  form.elements.id.value = "";
  form.elements.radiusMeters.value = "40";

  state.currentAudioUrl = "";
  state.currentTtsText = "";
  state.currentImageUrl = "";
  state.clearAudio = false;
  state.clearTts = false;
  state.clearImage = false;

  audioFileInput.disabled = false;
  ttsInput.disabled = false;
  if (imageFileInput) {
    imageFileInput.disabled = false;
  }

  updateGpsPreview();
  updateMediaConstraints();
  updateImageInputConstraints();
  renderExistingMedia();
  if (clearStatus) {
    setStatus("");
  }
}

function setStatus(text, isError = false) {
  statusText.textContent = text;
  statusText.className = isError ? "error" : "";
}

async function safeReadError(response) {
  try {
    const payload = await response.json();
    return payload.error || response.statusText;
  } catch {
    return response.statusText;
  }
}

function escapeHtml(text) {
  return (text || "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll("\"", "&quot;");
}
