(() => {
  const $ = (sel) => document.querySelector(sel);

  const nameEl = $("#poiName");
  const descEl = $("#poiDesc");
  const imageEl = $("#poiImage");
  const mapFrameEl = $("#mapFrame");
  const langSelectEl = $("#langSelect");
  const speakBtnEl = $("#speakBtn");
  const stopBtnEl = $("#stopBtn");
  const mapBtnEl = $("#mapBtn");
  const routeBtnEl = $("#routeBtn");
  const audioPlayerEl = $("#audioPlayer");
  const statusEl = $("#poiStatus");
  const appInstallPromptEl = $("#appInstallPrompt");
  const openAppBtnEl = $("#openAppBtn");
  const dismissInstallPromptBtnEl = $("#dismissInstallPromptBtn");

  const state = {
    poiId: "",
    lang: "vi",
    languages: [],
    poi: null,
    appHandOffDone: false,
  };

  const INSTALL_PROMPT_DISABLED_KEY = "poiDisableAppInstallPrompt";

  const setStatus = (text) => {
    statusEl.textContent = text || "";
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

  const toLangCode = (raw) => {
    const cleaned = String(raw || "").trim().toLowerCase();
    if (!cleaned) return "vi";
    const normalized = cleaned.replace("_", "-").split("-")[0];
    return normalized || "vi";
  };

  const buildMapEmbedUrl = (lat, lon) =>
    `https://maps.google.com/maps?q=${encodeURIComponent(`${lat},${lon}`)}&z=17&output=embed`;

  const buildMapPlaceUrl = (lat, lon) =>
    `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(`${lat},${lon}`)}`;

  const buildDirectionUrl = (lat, lon, origin) => {
    const params = new URLSearchParams();
    params.set("api", "1");
    params.set("destination", `${lat},${lon}`);
    if (origin) params.set("origin", origin);
    return `https://www.google.com/maps/dir/?${params.toString()}`;
  };

  const buildAppDeepLinkUrl = (poiId, langCode) => {
    const params = new URLSearchParams();
    params.set("id", String(poiId || "").trim());
    if (langCode) {
      params.set("lang", toLangCode(langCode));
    }
    return `foodstreet://open-poi?${params.toString()}`;
  };

  const shouldDisableInstallPrompt = () =>
    localStorage.getItem(INSTALL_PROMPT_DISABLED_KEY) === "1";

  const hideInstallPrompt = () => {
    if (appInstallPromptEl) {
      appInstallPromptEl.hidden = true;
    }
  };

  const showInstallPrompt = () => {
    if (appInstallPromptEl) {
      appInstallPromptEl.hidden = false;
    }
  };

  const tryOpenAppDeepLink = async (deepLinkUrl) => {
    if (!deepLinkUrl) return false;

    return new Promise((resolve) => {
      let completed = false;
      const finish = (opened) => {
        if (completed) return;
        completed = true;
        document.removeEventListener("visibilitychange", onVisibilityChange);
        window.removeEventListener("pagehide", onPageHide);
        resolve(Boolean(opened));
      };

      const onVisibilityChange = () => {
        if (document.visibilityState === "hidden") {
          finish(true);
        }
      };

      const onPageHide = () => finish(true);

      document.addEventListener("visibilitychange", onVisibilityChange, { once: true });
      window.addEventListener("pagehide", onPageHide, { once: true });

      window.location.href = deepLinkUrl;
      window.setTimeout(() => finish(document.visibilityState === "hidden"), 1600);
    });
  };

  const initAppHandOff = async () => {
    if (state.appHandOffDone || !state.poiId) {
      return;
    }

    state.appHandOffDone = true;
    hideInstallPrompt();

    if (shouldDisableInstallPrompt()) {
      return;
    }

    const opened = await tryOpenAppDeepLink(buildAppDeepLinkUrl(state.poiId, state.lang));
    if (!opened) {
      showInstallPrompt();
    }
  };

  const stopSpeaking = () => {
    if (window.speechSynthesis) {
      window.speechSynthesis.cancel();
    }
  };

  const speakText = (text, langCode) => {
    if (!window.speechSynthesis || !window.SpeechSynthesisUtterance) {
      setStatus("Trinh duyet khong ho tro TTS.");
      return;
    }

    stopSpeaking();
    const content = (text || "").trim();
    if (!content) {
      setStatus("Khong co noi dung de doc.");
      return;
    }

    const utterance = new SpeechSynthesisUtterance(content);
    utterance.lang = toLangCode(langCode);
    utterance.rate = 1;
    utterance.pitch = 1;
    utterance.onend = () => setStatus("");
    utterance.onerror = () => setStatus("Khong the phat TTS tren thiet bi nay.");
    window.speechSynthesis.speak(utterance);
  };

  const renderPoi = (poi) => {
    state.poi = poi;
    nameEl.textContent = poi.name || `POI #${poi.id || ""}`;
    descEl.textContent = poi.description || "Khong co mo ta.";

    if (poi.imageUrl) {
      imageEl.src = poi.imageUrl;
      imageEl.hidden = false;
    } else {
      imageEl.hidden = true;
      imageEl.removeAttribute("src");
    }

    if (poi.audioUrl) {
      audioPlayerEl.src = poi.audioUrl;
      audioPlayerEl.hidden = false;
    } else {
      audioPlayerEl.hidden = true;
      audioPlayerEl.removeAttribute("src");
    }

    mapFrameEl.src = buildMapEmbedUrl(poi.latitude, poi.longitude);
    mapBtnEl.href = poi.mapLink || buildMapPlaceUrl(poi.latitude, poi.longitude);
  };

  const fetchAndRenderPoi = async () => {
    if (!state.poiId) {
      throw new Error("URL khong co id POI.");
    }

    setStatus("Dang tai POI...");
    const data = await apiGet(`/api/public/pois/${encodeURIComponent(state.poiId)}?lang=${encodeURIComponent(state.lang)}`);
    renderPoi(data);
    setStatus("");
  };

  const initLanguages = async () => {
    const items = await apiGet("/api/languages");
    state.languages = Array.isArray(items) ? items : [];
    langSelectEl.innerHTML = "";
    for (const lang of state.languages) {
      const opt = document.createElement("option");
      opt.value = lang.code;
      opt.textContent = lang.label;
      langSelectEl.appendChild(opt);
    }

    if (!state.languages.some((x) => x.code === state.lang)) {
      state.lang = state.languages.some((x) => x.code === "vi") ? "vi" : (state.languages[0]?.code || "vi");
    }
    langSelectEl.value = state.lang;
  };

  const initEvents = () => {
    langSelectEl.addEventListener("change", async () => {
      state.lang = toLangCode(langSelectEl.value || "vi");
      const url = new URL(window.location.href);
      url.searchParams.set("lang", state.lang);
      window.history.replaceState({}, "", url.toString());
      try {
        await fetchAndRenderPoi();
      } catch (err) {
        setStatus(err?.message || String(err));
      }
    });

    speakBtnEl.addEventListener("click", () => {
      const poi = state.poi;
      if (!poi) return;
      const content = poi.ttsText || poi.description || poi.name;
      speakText(content, state.lang);
    });

    stopBtnEl.addEventListener("click", () => {
      stopSpeaking();
      setStatus("");
    });

    routeBtnEl.addEventListener("click", () => {
      const poi = state.poi;
      if (!poi) return;
      const lat = poi.latitude;
      const lon = poi.longitude;

      if (!navigator.geolocation) {
        window.open(buildDirectionUrl(lat, lon), "_blank", "noopener");
        return;
      }

      navigator.geolocation.getCurrentPosition(
        (pos) => {
          const origin = `${pos.coords.latitude},${pos.coords.longitude}`;
          window.open(buildDirectionUrl(lat, lon, origin), "_blank", "noopener");
        },
        () => {
          window.open(buildDirectionUrl(lat, lon), "_blank", "noopener");
        },
        { enableHighAccuracy: true, timeout: 8000, maximumAge: 30000 }
      );
    });

    openAppBtnEl?.addEventListener("click", async () => {
      await tryOpenAppDeepLink(buildAppDeepLinkUrl(state.poiId, state.lang));
    });

    dismissInstallPromptBtnEl?.addEventListener("click", () => {
      localStorage.setItem(INSTALL_PROMPT_DISABLED_KEY, "1");
      hideInstallPrompt();
    });
  };

  const init = async () => {
    const search = new URLSearchParams(window.location.search);
    state.poiId = (search.get("id") || "").trim();
    state.lang = toLangCode(search.get("lang") || "vi");

    await initAppHandOff();
    await initLanguages();
    initEvents();
    await fetchAndRenderPoi();
  };

  init().catch((err) => {
    setStatus(err?.message || String(err));
    nameEl.textContent = "Khong tim thay POI";
    descEl.textContent = "Vui long kiem tra lai ma QR hoac lien he quan tri vien.";
  });
})();
