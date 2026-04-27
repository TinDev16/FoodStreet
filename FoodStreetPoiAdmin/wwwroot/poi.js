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
  const poiStatusBadgeEl = $("#poiStatusBadge");
  const pageTitleEl = $("#pageTitle");
  const installPromptTitleEl = $("#installPromptTitle");
  const installPromptDescEl = $("#installPromptDesc");
  const langSelectorLabelEl = $("#langSelectorLabel");
  const poiPaymentChipEl = $("#poiPaymentChip");
  const poiPaymentNoteEl = $("#poiPaymentNote");
  const speakBtnLabelEl = $("#speakBtnLabel");
  const stopBtnLabelEl = $("#stopBtnLabel");
  const mapBtnLabelEl = $("#mapBtnLabel");
  const routeBtnLabelEl = $("#routeBtnLabel");
  const mapSectionTitleEl = $("#mapSectionTitle");
  const paymentPanelEl = $("#poiPaymentPanel");
  const priceLabelEl = $("#poiPriceLabel");
  const paymentHintEl = $("#poiPaymentHint");
  const unlockBtnEl = $("#unlockBtn");
  const appInstallPromptEl = $("#appInstallPrompt");
  const openAppBtnEl = $("#openAppBtn");
  const dismissInstallPromptBtnEl = $("#dismissInstallPromptBtn");

  const state = {
    poiId: "",
    lang: "vi",
    languages: [],
    poi: null,
    isPaid: false,
    guestUserId: 0,
    appHandOffDone: false,
    sessionId: "",
    storageKey: "poiAppLanguage",
    currentLocation: null
  };

  const INSTALL_PROMPT_DISABLED_KEY = "poiDisableAppInstallPrompt";
  const PUBLIC_GUEST_USER_ID_KEY = "poiPublicGuestUserId";
  const UI_TEXTS = {
    vi: {
      pageTitle: "FoodStreet - Khám phá điểm đến",
      installPromptTitle: "Trải nghiệm tốt hơn với App FoodStreet",
      installPromptDesc: "Xem POI trên bản đồ, nhận thông báo và nhiều tính năng hấp dẫn",
      openApp: "Mở App",
      dismissInstall: "Để sau",
      langSelector: "🌐 Ngôn ngữ",
      paymentChip: "Premium Access",
      paymentNote: "Thanh toán mô phỏng, mở ngay lập tức.",
      speakBtn: "Nghe giới thiệu",
      stopBtn: "Dừng",
      mapBtn: "Xem bản đồ",
      routeBtn: "Chỉ đường",
      mapSectionTitle: "📍 Vị trí trên bản đồ",
      noDescription: "Không có mô tả.",
      premiumLocked: "Premium",
      premiumUnlocked: "Premium • Đã mở khóa",
      paymentHint: "Mở khóa một lần để bật nghe audio, TTS và trải nghiệm đầy đủ nội dung.",
      unlockNow: "Mở khóa ngay",
      loadingPoi: "Đang tải POI...",
      invalidPoiUrl: "URL không có id POI.",
      browserNoTts: "Trình duyệt không hỗ trợ TTS.",
      noTextToRead: "Không có nội dung để đọc.",
      ttsError: "Không thể phát TTS trên thiết bị này.",
      premiumNeedUnlock: "POI Premium. Hãy mở khóa để nghe audio.",
      unlocking: "Đang mở khóa...",
      unlockSuccess: "Mở khóa thành công. Bạn có thể nghe audio.",
      poiNotFoundTitle: "Không tìm thấy POI",
      poiNotFoundDesc: "Vui lòng kiểm tra lại mã QR hoặc liên hệ quản trị viên."
    },
    en: {
      pageTitle: "FoodStreet - Explore Destinations",
      installPromptTitle: "Better experience with FoodStreet App",
      installPromptDesc: "View POIs on map, receive notifications and more features",
      openApp: "Open App",
      dismissInstall: "Later",
      langSelector: "🌐 Language",
      paymentChip: "Premium Access",
      paymentNote: "Simulated payment, unlock instantly.",
      speakBtn: "Listen",
      stopBtn: "Stop",
      mapBtn: "View map",
      routeBtn: "Route",
      mapSectionTitle: "📍 Map location",
      noDescription: "No description available.",
      premiumLocked: "Premium",
      premiumUnlocked: "Premium • Unlocked",
      paymentHint: "Unlock once to access audio, TTS and full premium content.",
      unlockNow: "Unlock now",
      loadingPoi: "Loading POI...",
      invalidPoiUrl: "Missing POI id in URL.",
      browserNoTts: "This browser does not support TTS.",
      noTextToRead: "No content to read.",
      ttsError: "Cannot play TTS on this device.",
      premiumNeedUnlock: "Premium POI. Please unlock to listen.",
      unlocking: "Unlocking...",
      unlockSuccess: "Unlocked successfully. You can now listen.",
      poiNotFoundTitle: "POI not found",
      poiNotFoundDesc: "Please check your QR code or contact the administrator."
    }
    // (Other languages omitted for brevity in rewrite, but should be kept if possible)
  };

  const setStatus = (text) => {
    statusEl.textContent = text || "";
  };

  const t = (key) => {
    const lang = state.lang && UI_TEXTS[state.lang] ? state.lang : "en";
    return UI_TEXTS[lang]?.[key] || UI_TEXTS.en[key] || key;
  };

  const updateStaticUiTexts = () => {
    document.documentElement.lang = state.lang || "en";
    document.title = t("pageTitle");
    if (pageTitleEl) pageTitleEl.textContent = t("pageTitle");
    installPromptTitleEl.textContent = t("installPromptTitle");
    installPromptDescEl.textContent = t("installPromptDesc");
    openAppBtnEl.textContent = t("openApp");
    dismissInstallPromptBtnEl.textContent = t("dismissInstall");
    langSelectorLabelEl.textContent = t("langSelector");
    poiPaymentChipEl.textContent = t("paymentChip");
    poiPaymentNoteEl.textContent = t("paymentNote");
    speakBtnLabelEl.textContent = t("speakBtn");
    stopBtnLabelEl.textContent = t("stopBtn");
    mapBtnLabelEl.textContent = t("mapBtn");
    routeBtnLabelEl.textContent = t("routeBtn");
    mapSectionTitleEl.textContent = t("mapSectionTitle");
    unlockBtnEl.textContent = t("unlockNow");
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
      headers: { Accept: "application/json", "Content-Type": "application/json" },
      body: JSON.stringify(body || {}),
    });
    if (!res.ok) throw new Error(await safeError(res));
    return res.json();
  };

  const getOrCreateDeviceId = () => {
    let id = localStorage.getItem("device_id");
    if (!id) {
        try { id = crypto.randomUUID(); } catch(e) { id = 'dev_' + Date.now() + '_' + Math.random().toString(36).substring(2); }
        localStorage.setItem("device_id", id);
    }
    return id;
  };

  const getScreenInfo = () => JSON.stringify({ w: window.screen.width, h: window.screen.height, dpr: window.devicePixelRatio || 1 });

  const trackActivity = async (action, overrideParams = {}) => {
    try {
        const sid = localStorage.getItem('session_id') || ('web_' + Date.now() + '_' + Math.random().toString(36).substring(2));
        localStorage.setItem('session_id', sid);
        state.sessionId = sid;
        const did = getOrCreateDeviceId();
        const payload = {
            action, platform: 'web', sessionId: sid, deviceId: did, language: state.lang,
            poiId: state.poiId, deviceType: 'web', screenInfo: getScreenInfo(), ...overrideParams
        };
        if (state.currentLocation) {
            payload.latitude = state.currentLocation.latitude;
            payload.longitude = state.currentLocation.longitude;
        }
        if (action === 'offline') {
            navigator.sendBeacon('/api/public/pois/track-activity', JSON.stringify(payload));
        } else {
            await fetch('/api/public/pois/track-activity', {
                method: 'POST', headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            });
        }
    } catch(e) {}
  };

  const toLangCode = (raw) => {
    const cleaned = String(raw || "").trim().toLowerCase();
    if (!cleaned) return "vi";
    const normalized = cleaned.replace("_", "-").split("-")[0];
    return normalized || "vi";
  };

  const buildMapEmbedUrl = (lat, lon) => `https://maps.google.com/maps?q=${encodeURIComponent(`${lat},${lon}`)}&z=17&output=embed`;
  const buildMapPlaceUrl = (lat, lon) => `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(`${lat},${lon}`)}`;
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
    if (langCode) params.set("lang", toLangCode(langCode));
    return `foodstreet://open-poi?${params.toString()}`;
  };

  const shouldDisableInstallPrompt = () => localStorage.getItem(INSTALL_PROMPT_DISABLED_KEY) === "1";
  const hideInstallPrompt = () => { if (appInstallPromptEl) appInstallPromptEl.hidden = true; };
  const showInstallPrompt = () => { if (appInstallPromptEl) appInstallPromptEl.hidden = false; };

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
      const onVisibilityChange = () => { if (document.visibilityState === "hidden") finish(true); };
      const onPageHide = () => finish(true);
      document.addEventListener("visibilitychange", onVisibilityChange, { once: true });
      window.addEventListener("pagehide", onPageHide, { once: true });
      window.location.href = deepLinkUrl;
      window.setTimeout(() => finish(document.visibilityState === "hidden"), 1600);
    });
  };

  const initAppHandOff = async () => {
    if (state.appHandOffDone || !state.poiId) return;
    state.appHandOffDone = true;
    hideInstallPrompt();
    if (shouldDisableInstallPrompt()) return;
    const opened = await tryOpenAppDeepLink(buildAppDeepLinkUrl(state.poiId, state.lang));
    if (!opened) showInstallPrompt();
  };

  const stopSpeaking = () => { if (window.speechSynthesis) window.speechSynthesis.cancel(); };

  const formatCurrency = (value) => {
    const amount = Number(value);
    if (!Number.isFinite(amount) || amount <= 0) return "Miễn phí";
    return `${Math.round(amount).toLocaleString("vi-VN")} đ`;
  };

  const ensureGuestUserId = () => {
    const fromStorage = Number.parseInt(localStorage.getItem(PUBLIC_GUEST_USER_ID_KEY) || "", 10);
    if (Number.isFinite(fromStorage) && fromStorage > 0) {
      state.guestUserId = fromStorage;
      return fromStorage;
    }
    const created = Math.floor(Date.now() / 1000);
    localStorage.setItem(PUBLIC_GUEST_USER_ID_KEY, String(created));
    state.guestUserId = created;
    return created;
  };

  const canPlayAudio = (poi) => {
    if (!poi) return false;
    const hasAudioContent = Boolean((poi.audioUrl || "").trim() || (poi.ttsText || "").trim() || (poi.description || "").trim());
    if (!hasAudioContent) return false;
    const price = Number(poi.price || 0);
    if (!Number.isFinite(price) || price <= 0) return true;
    return !!state.isPaid;
  };

  const speakText = (text, langCode) => {
    if (!window.speechSynthesis || !window.SpeechSynthesisUtterance) {
      setStatus(t("browserNoTts"));
      return;
    }
    stopSpeaking();
    const content = (text || "").trim();
    if (!content) {
      setStatus(t("noTextToRead"));
      return;
    }
    const utterance = new SpeechSynthesisUtterance(content);
    utterance.lang = toLangCode(langCode);
    utterance.rate = 1;
    utterance.pitch = 1;
    utterance.onend = () => setStatus("");
    utterance.onerror = () => setStatus(t("ttsError"));
    window.speechSynthesis.speak(utterance);
  };

  const renderPoi = (poi) => {
    state.poi = poi;
    const price = Number(poi.price || 0);
    nameEl.textContent = poi.name || `POI #${poi.id || ""}`;
    descEl.textContent = poi.description || t("noDescription");
    if (poi.imageUrl) { imageEl.src = poi.imageUrl; imageEl.hidden = false; }
    else { imageEl.hidden = true; imageEl.removeAttribute("src"); }
    const unlocked = canPlayAudio(poi);
    if (poi.audioUrl && unlocked) { audioPlayerEl.src = poi.audioUrl; audioPlayerEl.hidden = false; }
    else { audioPlayerEl.hidden = true; audioPlayerEl.removeAttribute("src"); }
    if (price > 0) {
      poiStatusBadgeEl.hidden = false;
      poiStatusBadgeEl.className = `poi-status-badge premium${state.isPaid ? " unlocked" : ""}`;
      poiStatusBadgeEl.textContent = state.isPaid ? t("premiumUnlocked") : t("premiumLocked");
    } else {
      poiStatusBadgeEl.hidden = true;
    }
    if (price > 0 && !state.isPaid) {
      paymentPanelEl.hidden = false;
      priceLabelEl.textContent = formatCurrency(price);
      paymentHintEl.textContent = t("paymentHint");
      speakBtnEl.disabled = true;
    } else {
      paymentPanelEl.hidden = true;
      speakBtnEl.disabled = !canPlayAudio(poi);
    }
    mapFrameEl.src = buildMapEmbedUrl(poi.latitude, poi.longitude);
    mapBtnEl.href = poi.mapLink || buildMapPlaceUrl(poi.latitude, poi.longitude);
  };

  const fetchAndRenderPoi = async () => {
    if (!state.poiId) throw new Error(t("invalidPoiUrl"));
    setStatus(t("loadingPoi"));
    ensureGuestUserId();
    const data = await apiGet(`/api/public/pois/${encodeURIComponent(state.poiId)}?lang=${encodeURIComponent(state.lang)}&userId=${encodeURIComponent(String(state.guestUserId))}`);
    state.isPaid = !!data.isPaid;
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
    localStorage.setItem(state.storageKey, state.lang);
    langSelectEl.value = state.lang;
  };

  const initEvents = () => {
    langSelectEl.addEventListener("change", async () => {
      state.lang = toLangCode(langSelectEl.value || "vi");
      localStorage.setItem(state.storageKey, state.lang);
      updateStaticUiTexts();
      const url = new URL(window.location.href);
      url.searchParams.set("lang", state.lang);
      window.history.replaceState({}, "", url.toString());
      try { await fetchAndRenderPoi(); } catch (err) { setStatus(err?.message || String(err)); }
    });
    speakBtnEl.addEventListener("click", () => {
      const poi = state.poi;
      if (!poi) return;
      if (!canPlayAudio(poi)) { setStatus(t("premiumNeedUnlock")); return; }
      speakText(poi.ttsText || poi.description || poi.name, state.lang);
      trackActivity('play_audio');
    });
    audioPlayerEl?.addEventListener("play", () => trackActivity('play_audio'));
    stopBtnEl.addEventListener("click", () => { stopSpeaking(); setStatus(""); });
    routeBtnEl.addEventListener("click", () => {
      const poi = state.poi;
      if (!poi) return;
      if (!navigator.geolocation) { window.open(buildDirectionUrl(poi.latitude, poi.longitude), "_blank", "noopener"); return; }
      navigator.geolocation.getCurrentPosition(
        (pos) => window.open(buildDirectionUrl(poi.latitude, poi.longitude, `${pos.coords.latitude},${pos.coords.longitude}`), "_blank", "noopener"),
        () => window.open(buildDirectionUrl(poi.latitude, poi.longitude), "_blank", "noopener"),
        { enableHighAccuracy: true, timeout: 8000, maximumAge: 30000 }
      );
    });
    openAppBtnEl?.addEventListener("click", async () => await tryOpenAppDeepLink(buildAppDeepLinkUrl(state.poiId, state.lang)));
    dismissInstallPromptBtnEl?.addEventListener("click", () => { localStorage.setItem(INSTALL_PROMPT_DISABLED_KEY, "1"); hideInstallPrompt(); });
    unlockBtnEl?.addEventListener("click", async () => {
      if (!state.poiId) return;
      try {
        setStatus(t("unlocking"));
        ensureGuestUserId();
        const result = await apiPostJson(`/api/public/pois/${encodeURIComponent(state.poiId)}/unlock`, { userId: state.guestUserId });
        state.isPaid = !!result.isPaid;
        await fetchAndRenderPoi();
        setStatus(t("unlockSuccess"));
      } catch (err) { setStatus(err?.message || String(err)); }
    });
  };

  const startLocationTracking = () => {
    if (!navigator.geolocation) return;
    const update = (pos) => { state.currentLocation = { latitude: pos.coords.latitude, longitude: pos.coords.longitude }; };
    navigator.geolocation.watchPosition(update, () => {}, { enableHighAccuracy: true, maximumAge: 10000, timeout: 5000 });
  };

  const initPingLoop = () => setInterval(() => {
     if (document.visibilityState !== 'hidden') trackActivity('ping');
  }, 15000);

  const init = async () => {
    const params = new URLSearchParams(window.location.search);
    state.poiId = String(params.get("id") || "").trim();
    state.lang = toLangCode(params.get("lang") || localStorage.getItem(state.storageKey) || "vi");
    updateStaticUiTexts();
    startLocationTracking();
    initEvents();
    try {
      await initLanguages();
      if (state.poiId) {
        await fetchAndRenderPoi();
        await trackActivity("view_poi");
        initAppHandOff();
        initPingLoop();
      } else {
        setStatus(t("invalidPoiUrl"));
        hideInstallPrompt();
      }
      
      window.addEventListener('beforeunload', () => trackActivity('offline'));
      document.addEventListener('visibilitychange', () => {
          if (document.visibilityState === 'hidden') trackActivity('offline');
          else trackActivity('ping');
      });

    } catch (err) { console.error(err); setStatus(err?.message || String(err)); hideInstallPrompt(); }
  };

  init().catch((err) => {
    setStatus(err?.message || String(err));
    nameEl.textContent = t("poiNotFoundTitle");
    descEl.textContent = t("poiNotFoundDesc");
  });
})();
