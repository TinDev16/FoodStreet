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
    },
    zh: {
      pageTitle: "FoodStreet - 探索目的地",
      installPromptTitle: "使用 FoodStreet 应用获得更佳体验",
      installPromptDesc: "在地图上查看地点，接收通知并体验更多功能",
      openApp: "打开应用",
      dismissInstall: "稍后",
      langSelector: "🌐 语言",
      paymentChip: "高级访问",
      paymentNote: "模拟支付，立即解锁。",
      speakBtn: "收听介绍",
      stopBtn: "停止",
      mapBtn: "查看地图",
      routeBtn: "导航",
      mapSectionTitle: "📍 地图位置",
      noDescription: "暂无描述。",
      premiumLocked: "高级内容",
      premiumUnlocked: "高级内容 • 已解锁",
      paymentHint: "一次解锁即可收听音频、TTS 和完整高级内容。",
      unlockNow: "立即解锁",
      loadingPoi: "正在加载地点...",
      invalidPoiUrl: "URL 中缺少 POI id。",
      browserNoTts: "当前浏览器不支持 TTS。",
      noTextToRead: "没有可朗读的内容。",
      ttsError: "此设备无法播放 TTS。",
      premiumNeedUnlock: "该地点为高级内容，请先解锁后收听。",
      unlocking: "正在解锁...",
      unlockSuccess: "解锁成功，您现在可以收听。",
      poiNotFoundTitle: "未找到地点",
      poiNotFoundDesc: "请检查二维码，或联系管理员。"
    },
    ja: {
      pageTitle: "FoodStreet - スポットを探索",
      installPromptTitle: "FoodStreet アプリでより快適に",
      installPromptDesc: "地図で POI を確認し、通知や便利な機能を利用できます",
      openApp: "アプリを開く",
      dismissInstall: "あとで",
      langSelector: "🌐 言語",
      paymentChip: "プレミアムアクセス",
      paymentNote: "模擬決済ですぐに解除されます。",
      speakBtn: "紹介を聞く",
      stopBtn: "停止",
      mapBtn: "地図を見る",
      routeBtn: "経路案内",
      mapSectionTitle: "📍 地図上の位置",
      noDescription: "説明がありません。",
      premiumLocked: "プレミアム",
      premiumUnlocked: "プレミアム • 解除済み",
      paymentHint: "1回の解除で音声、TTS、すべてのプレミアム内容を利用できます。",
      unlockNow: "今すぐ解除",
      loadingPoi: "POI を読み込み中...",
      invalidPoiUrl: "URL に POI id がありません。",
      browserNoTts: "このブラウザは TTS に対応していません。",
      noTextToRead: "読み上げる内容がありません。",
      ttsError: "この端末では TTS を再生できません。",
      premiumNeedUnlock: "この POI はプレミアムです。先に解除してください。",
      unlocking: "解除中...",
      unlockSuccess: "解除に成功しました。再生できます。",
      poiNotFoundTitle: "POI が見つかりません",
      poiNotFoundDesc: "QR コードを確認するか管理者へ連絡してください。"
    },
    ru: {
      pageTitle: "FoodStreet - Исследуйте места",
      installPromptTitle: "Лучший опыт с приложением FoodStreet",
      installPromptDesc: "Смотрите POI на карте, получайте уведомления и используйте другие функции",
      openApp: "Открыть приложение",
      dismissInstall: "Позже",
      langSelector: "🌐 Язык",
      paymentChip: "Премиум-доступ",
      paymentNote: "Виртуальная оплата, мгновенная разблокировка.",
      speakBtn: "Слушать",
      stopBtn: "Стоп",
      mapBtn: "Открыть карту",
      routeBtn: "Маршрут",
      mapSectionTitle: "📍 Расположение на карте",
      noDescription: "Описание отсутствует.",
      premiumLocked: "Премиум",
      premiumUnlocked: "Премиум • Разблокировано",
      paymentHint: "Разблокируйте один раз, чтобы получить аудио, TTS и полный премиум-контент.",
      unlockNow: "Разблокировать",
      loadingPoi: "Загрузка POI...",
      invalidPoiUrl: "В URL отсутствует id POI.",
      browserNoTts: "Браузер не поддерживает TTS.",
      noTextToRead: "Нет текста для озвучивания.",
      ttsError: "Невозможно воспроизвести TTS на этом устройстве.",
      premiumNeedUnlock: "Премиум POI. Сначала разблокируйте, чтобы слушать.",
      unlocking: "Разблокировка...",
      unlockSuccess: "Успешно разблокировано. Теперь можно слушать.",
      poiNotFoundTitle: "POI не найден",
      poiNotFoundDesc: "Проверьте QR-код или свяжитесь с администратором."
    },
    ko: {
      pageTitle: "FoodStreet - 명소 탐색",
      installPromptTitle: "FoodStreet 앱으로 더 나은 경험",
      installPromptDesc: "지도에서 POI를 보고 알림과 다양한 기능을 이용하세요",
      openApp: "앱 열기",
      dismissInstall: "나중에",
      langSelector: "🌐 언어",
      paymentChip: "프리미엄 이용권",
      paymentNote: "가상 결제로 즉시 잠금 해제됩니다.",
      speakBtn: "소개 듣기",
      stopBtn: "정지",
      mapBtn: "지도 보기",
      routeBtn: "길찾기",
      mapSectionTitle: "📍 지도 위치",
      noDescription: "설명이 없습니다.",
      premiumLocked: "프리미엄",
      premiumUnlocked: "프리미엄 • 잠금 해제됨",
      paymentHint: "한 번 해제로 오디오, TTS 및 전체 프리미엄 콘텐츠를 이용할 수 있습니다.",
      unlockNow: "지금 해제",
      loadingPoi: "POI 불러오는 중...",
      invalidPoiUrl: "URL에 POI id가 없습니다.",
      browserNoTts: "이 브라우저는 TTS를 지원하지 않습니다.",
      noTextToRead: "읽을 내용이 없습니다.",
      ttsError: "이 기기에서 TTS를 재생할 수 없습니다.",
      premiumNeedUnlock: "프리미엄 POI입니다. 먼저 해제해 주세요.",
      unlocking: "잠금 해제 중...",
      unlockSuccess: "잠금 해제되었습니다. 이제 들을 수 있습니다.",
      poiNotFoundTitle: "POI를 찾을 수 없습니다",
      poiNotFoundDesc: "QR 코드를 확인하거나 관리자에게 문의해 주세요."
    }
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
    if (pageTitleEl) {
      pageTitleEl.textContent = t("pageTitle");
    }

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

    if (poi.imageUrl) {
      imageEl.src = poi.imageUrl;
      imageEl.hidden = false;
    } else {
      imageEl.hidden = true;
      imageEl.removeAttribute("src");
    }

    const unlocked = canPlayAudio(poi);
    if (poi.audioUrl && unlocked) {
      audioPlayerEl.src = poi.audioUrl;
      audioPlayerEl.hidden = false;
    } else {
      audioPlayerEl.hidden = true;
      audioPlayerEl.removeAttribute("src");
    }

    if (price > 0) {
      poiStatusBadgeEl.hidden = false;
      poiStatusBadgeEl.className = `poi-status-badge premium${state.isPaid ? " unlocked" : ""}`;
      poiStatusBadgeEl.textContent = state.isPaid ? t("premiumUnlocked") : t("premiumLocked");
    } else {
      poiStatusBadgeEl.hidden = true;
      poiStatusBadgeEl.className = "poi-status-badge";
      poiStatusBadgeEl.textContent = "";
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
    if (!state.poiId) {
      throw new Error(t("invalidPoiUrl"));
    }

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
    langSelectEl.value = state.lang;
  };

  const initEvents = () => {
    langSelectEl.addEventListener("change", async () => {
      state.lang = toLangCode(langSelectEl.value || "vi");
      updateStaticUiTexts();
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
      if (!canPlayAudio(poi)) {
        setStatus(t("premiumNeedUnlock"));
        return;
      }
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

    unlockBtnEl?.addEventListener("click", async () => {
      if (!state.poiId) return;
      try {
        setStatus(t("unlocking"));
        ensureGuestUserId();
        const result = await apiPostJson(`/api/public/pois/${encodeURIComponent(state.poiId)}/unlock`, { userId: state.guestUserId });
        state.isPaid = !!result.isPaid;
        await fetchAndRenderPoi();
        setStatus(t("unlockSuccess"));
      } catch (err) {
        setStatus(err?.message || String(err));
      }
    });
  };

  const init = async () => {
    const search = new URLSearchParams(window.location.search);
    state.poiId = (search.get("id") || "").trim();
    state.lang = toLangCode(search.get("lang") || "vi");
    updateStaticUiTexts();

    await initAppHandOff();
    await initLanguages();
    initEvents();
    await fetchAndRenderPoi();
  };

  init().catch((err) => {
    setStatus(err?.message || String(err));
    nameEl.textContent = t("poiNotFoundTitle");
    descEl.textContent = t("poiNotFoundDesc");
  });
})();
