(() => {
  const $ = (sel) => document.querySelector(sel);
  const formEl = $("#poiForm");
  const statusEl = $("#status");
  const rowsEl = $("#poiRows");
  const listHintEl = $("#listHint");
  const createBtn = $("#createBtn");
  const resetBtn = $("#resetBtn");
  const sourceLangSelect = $("#sourceLang");

  const state = {
    languages: [],
    selectedSourceLang: "vi",
    current: {
      id: "",
      coreImageUrl: "",
      coreAudioUrl: "",
      translationsByLang: {},
    },
  };

  const setStatus = (msg, isError = false) => {
    statusEl.textContent = msg || "";
    statusEl.classList.toggle("error", !!isError);
  };

  const safeError = async (res) => {
    try {
      const j = await res.json();
      return j?.error ? `${j.error}${j.detail ? `: ${j.detail}` : ""}` : JSON.stringify(j);
    } catch {
      return `${res.status} ${res.statusText}`;
    }
  };

  const apiGet = async (url) => {
    const res = await fetch(url, { headers: { Accept: "application/json" } });
    if (!res.ok) throw new Error(await safeError(res));
    return res.json();
  };

  const apiPostJson = async (url, body) => {
    const res = await fetch(url, {
      method: "POST",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      body: JSON.stringify(body),
    });
    if (!res.ok) throw new Error(await safeError(res));
    return res.json();
  };

  const apiDelete = async (url) => {
    const res = await fetch(url, { method: "DELETE", headers: { Accept: "application/json" } });
    if (!res.ok) throw new Error(await safeError(res));
    return res.json().catch(() => ({}));
  };

  const ensureTranslationState = (langCode) => {
    const code = String(langCode || "vi").toLowerCase();
    if (!state.current.translationsByLang[code]) {
      state.current.translationsByLang[code] = {
        name: "",
        description: "",
        ttsText: "",
        audioUrl: "",
      };
    }
    return state.current.translationsByLang[code];
  };

  const setCurrentLink = (anchor, emptySpan, url) => {
    if (url) {
      anchor.href = url;
      anchor.textContent = url;
      anchor.hidden = false;
      emptySpan.hidden = true;
    } else {
      anchor.hidden = true;
      emptySpan.hidden = false;
    }
  };

  const parseGps = (raw) => {
    const cleaned = (raw || "").trim().replace(/[()]/g, "");
    if (!cleaned) return null;
    const parts = cleaned.split(/[\s,;]+/).filter(Boolean);
    if (parts.length < 2) return null;

    const lat = Number.parseFloat(parts[0]);
    const lon = Number.parseFloat(parts[1]);
    if (!Number.isFinite(lat) || !Number.isFinite(lon)) return null;
    if (lat < -90 || lat > 90 || lon < -180 || lon > 180) return null;

    return { lat, lon };
  };

  const setGeoFields = (lat, lon) => {
    formEl.elements.namedItem("latitude").value = String(lat);
    formEl.elements.namedItem("longitude").value = String(lon);
    formEl.elements.namedItem("mapLink").value = `https://maps.google.com/?q=${lat},${lon}`;
  };

  const updateFromGpsInput = () => {
    const gpsInput = formEl.elements.namedItem("gps");
    const latInput = formEl.elements.namedItem("latitude");
    const lonInput = formEl.elements.namedItem("longitude");
    const mapLinkInput = formEl.elements.namedItem("mapLink");
    const raw = (gpsInput.value || "").trim();
    if (!raw) {
      latInput.value = "";
      lonInput.value = "";
      mapLinkInput.value = "";
      gpsInput.setCustomValidity("");
      return false;
    }

    const parsed = parseGps(gpsInput.value);
    if (!parsed) {
      gpsInput.setCustomValidity("GPS khong hop le. Vi du: 10.761895379862327, 106.70358792842893");
      return false;
    }

    gpsInput.setCustomValidity("");
    setGeoFields(parsed.lat, parsed.lon);
    return true;
  };

  const saveSourceFieldsToState = () => {
    const tr = ensureTranslationState(state.selectedSourceLang);
    tr.name = (formEl.elements.namedItem("sourceName").value || "").trim();
    tr.description = (formEl.elements.namedItem("sourceDescription").value || "").trim();
    tr.ttsText = (formEl.elements.namedItem("sourceTtsText").value || "").trim();
  };

  const loadSourceFieldsFromState = () => {
    const tr = ensureTranslationState(state.selectedSourceLang);
    formEl.elements.namedItem("sourceName").value = tr.name || "";
    formEl.elements.namedItem("sourceDescription").value = tr.description || "";
    formEl.elements.namedItem("sourceTtsText").value = tr.ttsText || "";
  };

  const setActiveSourceLang = (langCode, options = {}) => {
    if (!options.skipSave) {
      saveSourceFieldsToState();
    }
    state.selectedSourceLang = String(langCode || "vi").toLowerCase();
    sourceLangSelect.value = state.selectedSourceLang;
    loadSourceFieldsFromState();
  };

  const renderList = (items) => {
    rowsEl.innerHTML = "";
    listHintEl.textContent = `${items.length} POI`;

    for (const item of items) {
      const tr = document.createElement("tr");
      tr.innerHTML = `
        <td class="mono">${escapeHtml(item.id)}</td>
        <td>${escapeHtml(item.nameVi || "")}</td>
        <td class="mono">${escapeHtml(String(item.latitude))}, ${escapeHtml(String(item.longitude))}</td>
        <td class="mono">${escapeHtml(String(item.radiusMeters))}</td>
        <td class="mono">${escapeHtml(String(item.priority))}</td>
        <td>${item.isActive ? "YES" : ""}</td>
        <td>
          <button type="button" class="secondary" data-action="edit" data-id="${escapeAttr(item.id)}">Sua</button>
          <button type="button" class="danger" data-action="del" data-id="${escapeAttr(item.id)}">Xoa</button>
        </td>
      `;
      rowsEl.appendChild(tr);
    }
  };

  const escapeHtml = (s) => String(s ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
  const escapeAttr = (s) => escapeHtml(s).replace(/"/g, "&quot;");

  const loadList = async () => {
    const items = await apiGet("/api/pois/admin");
    renderList(items);
  };

  const resetForm = () => {
    formEl.reset();
    state.current.id = "";
    state.current.coreImageUrl = "";
    state.current.coreAudioUrl = "";
    state.current.translationsByLang = {};

    for (const lang of state.languages) {
      ensureTranslationState(lang.code);
    }

    const defaultLang = state.languages.some((x) => x.code === "vi") ? "vi" : (state.languages[0]?.code || "vi");
    state.selectedSourceLang = defaultLang;
    sourceLangSelect.value = defaultLang;

    $("#currentImageLink").hidden = true;
    $("#currentImageEmpty").hidden = false;
    $("#currentAudioLink").hidden = true;
    $("#currentAudioEmpty").hidden = false;

    formEl.elements.namedItem("gps").value = "";
    formEl.elements.namedItem("latitude").value = "";
    formEl.elements.namedItem("longitude").value = "";
    formEl.elements.namedItem("mapLink").value = "";
    loadSourceFieldsFromState();
    setStatus("");
  };

  const loadPoi = async (id) => {
    const data = await apiGet(`/api/pois/${encodeURIComponent(id)}`);
    resetForm();

    state.current.id = data.id || "";
    state.current.coreImageUrl = data.imageUrl || "";
    state.current.coreAudioUrl = data.audioUrl || "";

    formEl.elements.namedItem("id").value = data.id || "";
    formEl.elements.namedItem("gps").value = `${data.latitude}, ${data.longitude}`;
    updateFromGpsInput();

    formEl.elements.namedItem("radiusMeters").value = String(data.radiusMeters ?? 15);
    formEl.elements.namedItem("priority").value = String(data.priority ?? 0);
    const isActiveInput = formEl.elements.namedItem("isActive");
    if (isActiveInput) {
      isActiveInput.checked = !!data.isActive;
    }

    setCurrentLink($("#currentImageLink"), $("#currentImageEmpty"), state.current.coreImageUrl);
    setCurrentLink($("#currentAudioLink"), $("#currentAudioEmpty"), state.current.coreAudioUrl);

    const translations = Array.isArray(data.translations) ? data.translations : [];
    for (const t of translations) {
      if (!t || !t.langCode) continue;
      const code = String(t.langCode).toLowerCase();
      state.current.translationsByLang[code] = {
        name: t.name || "",
        description: t.description || "",
        ttsText: t.ttsText || "",
        audioUrl: t.audioUrl || "",
      };
    }

    const preferred = state.current.translationsByLang.vi ? "vi" : (translations[0]?.langCode || state.selectedSourceLang || "vi");
    setActiveSourceLang(preferred, { skipSave: true });

    setStatus(`Loaded POI #${id}`);
  };

  const uploadFile = async (file, kind, lang) => {
    const fd = new FormData();
    fd.append("file", file);
    const qs = new URLSearchParams({ kind });
    if (lang) qs.set("lang", lang);

    const res = await fetch(`/api/uploads?${qs.toString()}`, { method: "POST", body: fd });
    if (!res.ok) throw new Error(await safeError(res));
    const j = await res.json();
    return j.url;
  };

  const buildPayloadAndUpload = async () => {
    saveSourceFieldsToState();
    if (!updateFromGpsInput()) {
      throw new Error("GPS khong hop le.");
    }

    const id = formEl.elements.namedItem("id").value.trim();
    const latitude = Number.parseFloat(formEl.elements.namedItem("latitude").value);
    const longitude = Number.parseFloat(formEl.elements.namedItem("longitude").value);
    const radiusMeters = Number.parseFloat(formEl.elements.namedItem("radiusMeters").value);
    const priority = Number.parseInt(formEl.elements.namedItem("priority").value, 10);
    const mapLink = (formEl.elements.namedItem("mapLink").value || "").trim();
    const isActive = formEl.elements.namedItem("isActive")?.checked ?? false;

    let imageUrl = state.current.coreImageUrl;
    let audioUrl = state.current.coreAudioUrl;

    const removeImage = formEl.elements.namedItem("removeImage")?.checked ?? false;
    const removeAudio = formEl.elements.namedItem("removeAudio")?.checked ?? false;
    const imageFile = formEl.elements.namedItem("imageFile")?.files?.[0] || null;
    const audioFile = formEl.elements.namedItem("audioFile")?.files?.[0] || null;

    if (removeImage) imageUrl = "";
    if (removeAudio) audioUrl = "";
    if (imageFile) imageUrl = await uploadFile(imageFile, "image", null);
    if (audioFile) audioUrl = await uploadFile(audioFile, "audio", null);

    const source = ensureTranslationState(state.selectedSourceLang);

    const translations = state.languages.map((lang) => {
      const tr = ensureTranslationState(lang.code);
      return {
        langCode: lang.code,
        name: tr.name || "",
        description: tr.description || "",
        ttsText: tr.ttsText || "",
        audioUrl: tr.audioUrl || "",
      };
    });

    return {
      id: id || null,
      latitude,
      longitude,
      radiusMeters: Number.isFinite(radiusMeters) ? radiusMeters : 15,
      priority: Number.isFinite(priority) ? priority : 0,
      mapLink: mapLink || null,
      imageUrl,
      audioUrl,
      isActive,
      sourceLangCode: state.selectedSourceLang,
      sourceName: source.name || "",
      sourceDescription: source.description || "",
      sourceTtsText: source.ttsText || "",
      translations,
    };
  };

  const buildSourceLangOptions = () => {
    sourceLangSelect.innerHTML = "";
    for (const lang of state.languages) {
      const opt = document.createElement("option");
      opt.value = lang.code;
      opt.textContent = lang.label;
      sourceLangSelect.appendChild(opt);
    }
  };

  const wireEvents = () => {
    sourceLangSelect.addEventListener("change", () => setActiveSourceLang(sourceLangSelect.value || "vi"));

    const gpsInput = formEl.elements.namedItem("gps");
    gpsInput.addEventListener("input", () => updateFromGpsInput());
    gpsInput.addEventListener("blur", () => updateFromGpsInput());

    createBtn.addEventListener("click", () => resetForm());
    resetBtn.addEventListener("click", () => resetForm());

    rowsEl.addEventListener("click", async (e) => {
      const btn = e.target?.closest("button[data-action]");
      if (!btn) return;
      const action = btn.dataset.action;
      const id = btn.dataset.id;
      if (!id) return;

      try {
        if (action === "edit") {
          await loadPoi(id);
        } else if (action === "del") {
          if (!confirm(`Xoa POI #${id}?`)) return;
          setStatus("Deleting...");
          await apiDelete(`/api/pois/${encodeURIComponent(id)}`);
          await loadList();
          resetForm();
          setStatus(`Deleted POI #${id}`);
        }
      } catch (err) {
        setStatus(err?.message || String(err), true);
      }
    });

    formEl.addEventListener("submit", async (e) => {
      e.preventDefault();
      updateFromGpsInput();
      if (!formEl.reportValidity()) return;

      setStatus("Saving...");
      try {
        const payload = await buildPayloadAndUpload();
        const res = await apiPostJson("/api/pois", payload);
        await loadList();
        await loadPoi(res.id);
        setStatus(`Saved POI #${res.id}`);
      } catch (err) {
        setStatus(err?.message || String(err), true);
      }
    });
  };

  const init = async () => {
    setStatus("Loading...");
    state.languages = await apiGet("/api/languages");
    buildSourceLangOptions();

    for (const lang of state.languages) {
      ensureTranslationState(lang.code);
    }

    const defaultLang = state.languages.some((x) => x.code === "vi") ? "vi" : (state.languages[0]?.code || "vi");
    state.selectedSourceLang = defaultLang;
    sourceLangSelect.value = defaultLang;

    wireEvents();
    resetForm();
    await loadList();
    setStatus("");
  };

  init().catch((err) => setStatus(err?.message || String(err), true));
})();
