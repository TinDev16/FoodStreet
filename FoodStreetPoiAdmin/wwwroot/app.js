const form = document.getElementById("poiForm");
const rows = document.getElementById("poiRows");
const statusText = document.getElementById("status");
const resetBtn = document.getElementById("resetBtn");
const gpsInput = form.elements.gps;
const latPreview = document.getElementById("latPreview");
const lonPreview = document.getElementById("lonPreview");

gpsInput.addEventListener("input", updateGpsPreview);
resetBtn.addEventListener("click", () => resetForm(true));
form.addEventListener("submit", onSubmit);

loadPois();
updateGpsPreview();

async function loadPois() {
  const response = await fetch("/api/shops");
  const list = await response.json();
  rows.innerHTML = "";

  for (const item of list) {
    const tr = document.createElement("tr");
    const audioLabel = item.audioUrl ? "Audio file" : (item.ttsText ? "TTS text" : "Khong");
    tr.innerHTML = `
      <td>${escapeHtml(item.shopName)}</td>
      <td>${item.latitude}, ${item.longitude}</td>
      <td>${item.radiusMeters}</td>
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
  form.elements.ttsText.value = item.ttsText || "";
  form.elements.audioFile.value = "";
  updateGpsPreview();
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

  const body = new FormData();
  if (form.elements.id.value.trim()) {
    body.append("id", form.elements.id.value.trim());
  }

  body.append("shopName", form.elements.shopName.value.trim());
  body.append("gps", form.elements.gps.value.trim());
  body.append("radiusMeters", form.elements.radiusMeters.value);
  body.append("description", form.elements.description.value.trim());
  body.append("ttsText", form.elements.ttsText.value.trim());

  const file = form.elements.audioFile.files[0];
  if (file) {
    body.append("audioFile", file);
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

function resetForm(clearStatus) {
  form.reset();
  form.elements.id.value = "";
  form.elements.radiusMeters.value = "40";
  if (clearStatus) {
    setStatus("");
  }
  updateGpsPreview();
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
