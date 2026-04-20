const token = localStorage.getItem('adminToken');
if (!token) {
  window.location.replace('/');
}

document.getElementById('logoutBtn')?.addEventListener('click', () => {
  localStorage.removeItem('adminToken');
  window.location.replace('/');
});

const themeToggleBtn = document.getElementById('themeToggleBtn');
if (themeToggleBtn) {
  const currentTheme = localStorage.getItem('poi_theme') || 'light';
  document.documentElement.setAttribute('data-theme', currentTheme);
  
  themeToggleBtn.addEventListener('click', () => {
    const newTheme = document.documentElement.getAttribute('data-theme') === 'dark' ? 'light' : 'dark';
    document.documentElement.setAttribute('data-theme', newTheme);
    localStorage.setItem('poi_theme', newTheme);
    
    // Update all charts in the registry
    Object.values(charts).forEach(c => {
        if (c) c.updateOptions({ theme: { mode: newTheme } });
    });
    // Legacy support for vars not in registry yet
    ['activityChartVar', 'hourlyChartVar'].forEach(key => {
        if (window[key]) window[key].updateOptions({ theme: { mode: newTheme } });
    });
  });
}

function parseJwt(token) {
  try {
    const base64Url = token.split('.')[1];
    const base64 = base64Url.replace(/-/g, '+').replace(/_/g, '/');
    const jsonPayload = decodeURIComponent(atob(base64).split('').map(function(c) {
      return '%' + ('00' + c.charCodeAt(0).toString(16)).slice(-2);
    }).join(''));
    return JSON.parse(jsonPayload);
  } catch (e) { return null; }
}

const payload = parseJwt(token);
if (payload) {
  document.getElementById('sidebarUserName').textContent = payload.admin_full_name || payload.admin_username || 'Admin';
  const role = (payload.admin_role || "").toLowerCase();
  const isSuper = role === 'superadmin';
  const isOwner = role === 'owner';
  if (isSuper) document.getElementById('ownerManageNav').hidden = false;
  if (isSuper || isOwner) document.getElementById('qrManageNav').hidden = false;
  if (document.getElementById('monitoringNav')) document.getElementById('monitoringNav').hidden = !(isSuper || isOwner);
  if (isOwner) document.getElementById('sidebarUserRole').textContent = 'Chủ sở hữu';
}

// --- STATE ---
const charts = {};
let selectedAction = null;
let logPage = 0;
let logPageSize = 10;
let totalLogs = 0;

const ACTION_META = {
  online: { label: 'User online',     countHeader: 'Số user online', icon: 'fa-signal' },
  audio:  { label: 'Lượt nghe Audio', countHeader: 'Lượt nghe',      icon: 'fa-headphones' },
  qr:     { label: 'Lượt quét QR',    countHeader: 'Lượt quét',      icon: 'fa-qrcode' },
  view:   { label: 'Lượt xem POI',    countHeader: 'Lượt xem',       icon: 'fa-eye' },
};

// --- DOM ELEMENTS ---
const filterPlatform = document.getElementById('filterPlatform');
const filterPeriod = document.getElementById('filterPeriod');
const filterFrom = document.getElementById('filterFrom');
const filterTo = document.getElementById('filterTo');
const filterPoiSort = document.getElementById('filterPoiSort');
const btnRefresh = document.getElementById('btnRefresh');
const customDateRange = document.getElementById('customDateRange');

const btnPrevLog = document.getElementById('btnPrevLog');
const btnNextLog = document.getElementById('btnNextLog');
const logPaginationInfo = document.getElementById('logPaginationInfo');

// --- EVENT LISENERS ---
filterPeriod?.addEventListener('change', () => {
  customDateRange.style.display = filterPeriod.value === 'custom' ? 'flex' : 'none';
});

btnRefresh?.addEventListener('click', () => {
    logPage = 0;
    loadDashboard();
});

btnPrevLog?.addEventListener('click', () => {
    if (logPage > 0) {
        logPage--;
        loadDashboard();
    }
});

btnNextLog?.addEventListener('click', () => {
    if ((logPage + 1) * logPageSize < totalLogs) {
        logPage++;
        loadDashboard();
    }
});

document.getElementById('btnExportLogs')?.addEventListener('click', () => exportToCsv());

document.getElementById('btnToggleLogCollapse')?.addEventListener('click', () => {
    const content = document.getElementById('logCollapsibleContent');
    const icon = document.querySelector('#btnToggleLogCollapse i.fa-chevron-down');
    if (content.style.display === 'none') {
        content.style.display = 'block';
        icon.style.transform = 'rotate(0deg)';
    } else {
        content.style.display = 'none';
        icon.style.transform = 'rotate(-90deg)';
    }
});

document.querySelectorAll('.stat-card.clickable').forEach(card => {
  const handler = () => {
    const next = card.dataset.action;
    selectedAction = (selectedAction === next) ? null : next;
    logPage = 0;
    applyCardActiveState();
    loadDashboard();
  };
  card.addEventListener('click', handler);
});

// --- CORE LOGIC ---
function applyCardActiveState() {
  document.querySelectorAll('.stat-card.clickable').forEach(card => {
    card.classList.toggle('active', card.dataset.action === (selectedAction || 'all'));
  });
}

function updateChartLabels() {
  const hourlySub = document.getElementById('hourlyChartSubtitle');
  const rankSub = document.getElementById('poiRankingSubtitle');
  const rankHeader = document.getElementById('poiRankingCountHeader');
  const meta = selectedAction && selectedAction !== 'all' ? ACTION_META[selectedAction] : null;

  if (hourlySub) {
    if (selectedAction === 'online') hourlySub.textContent = 'Số user duy nhất online trong mỗi khung giờ';
    else if (meta) hourlySub.textContent = `Chỉ tính: ${meta.label}`;
    else hourlySub.textContent = 'Tất cả tương tác (trừ ping)';
  }
  if (rankSub) {
    if (selectedAction === 'online') rankSub.textContent = 'Không áp dụng cho Online thực tế';
    else if (meta) rankSub.textContent = `Chỉ tính: ${meta.label}`;
    else rankSub.textContent = 'Điểm = Quét QR × 3 + Nghe Audio × 2 + Xem POI × 1';
  }
  if (rankHeader) rankHeader.textContent = meta ? meta.countHeader : 'Tổng tương tác';
}

function generateDateRange(startDate, endDate) {
  const dates = [];
  const start = new Date(startDate + "T00:00:00Z");
  const end = new Date(endDate + "T00:00:00Z");
  let current = new Date(start);
  while (current <= end) {
    dates.push(current.toISOString().split('T')[0]);
    current.setUTCDate(current.getUTCDate() + 1);
  }
  return dates;
}

async function loadDashboard() {
  try {
    const params = new URLSearchParams();
    if (filterPlatform.value !== 'all') params.set('platform', filterPlatform.value);
    if (filterPeriod.value) params.set('period', filterPeriod.value);
    if (filterPeriod.value === 'custom') {
      params.set('from', filterFrom.value);
      params.set('to', filterTo.value);
    }
    if (filterPoiSort.value) params.set('poiSort', filterPoiSort.value);
    if (selectedAction && selectedAction !== 'all') params.set('action', selectedAction);
    
    // Pagination params
    params.set('page', logPage);
    params.set('pageSize', logPageSize);

    const res = await fetch('/api/admin/reports/user-activities?' + params.toString(), {
      headers: { 'Authorization': 'Bearer ' + token }
    });
    
    if (res.status === 401 || res.status === 403) {
      localStorage.removeItem('adminToken');
      window.location.replace('/');
      return;
    }

    if (!res.ok) throw new Error('Failed to load dashboard data');
    const data = await res.json();
    
    // Summary Cards
    document.getElementById('valOnlineNow').textContent = data.onlineNow || 0;
    document.getElementById('valPeriodAudioPlays').textContent = data.periodAudioPlays || 0;
    document.getElementById('valPeriodQrScans').textContent = data.periodQrScans || 0;
    document.getElementById('valPeriodViews').textContent = data.periodViews || 0;
    
    totalLogs = data.totalLogCount || 0;
    
    updateChartLabels();
    renderMainChart(data.chartData || [], data.startDate, data.endDate);
    renderHourlyChart(data.hourlyData || [], selectedAction);
    
    renderDonutChart('langChart', data.langStats || []);

    renderPoiRanking(data.topPois || [], selectedAction);
    renderDetailedLogs(data.recentLogs || []);
    
    // Pagination UI
    const totalPages = Math.ceil(totalLogs / logPageSize);
    logPaginationInfo.textContent = totalLogs > 0 ? `Trang ${logPage + 1} / ${totalPages} (Tổng ${totalLogs} lượt)` : 'Không có dữ liệu';
    btnPrevLog.disabled = logPage <= 0;
    btnNextLog.disabled = (logPage + 1) >= totalPages;

  } catch(e) { console.error(e); }
}

function renderMainChart(chartData, startDate, endDate) {
  const dates = (startDate && endDate) 
    ? generateDateRange(startDate, endDate)
    : [...new Set(chartData.map(d => d.date))].sort();
  
  const audioPlays = dates.map(dt => chartData.filter(d => d.date === dt && d.action === 'play_audio').reduce((s, d) => s + d.count, 0));
  const qrScans = dates.map(dt => chartData.filter(d => d.date === dt && d.action === 'scan_qr').reduce((s, d) => s + d.count, 0));
  const views = dates.map(dt => chartData.filter(d => d.date === dt && d.action === 'view_poi').reduce((s, d) => s + d.count, 0));

  const isDark = document.documentElement.getAttribute('data-theme') === 'dark';
  const formatSeries = (data) => data.map((v, i) => [new Date(dates[i] + "T00:00:00Z").getTime(), v]);

  const options = {
    series: [
      { name: 'Xem POI', data: formatSeries(views) },
      { name: 'Nghe Audio', data: formatSeries(audioPlays) },
      { name: 'Quét QR', data: formatSeries(qrScans) }
    ],
    colors: ['#0ea5e9', '#f59e0b', '#10b981'],
    chart: { type: 'area', height: 350, fontFamily: 'Outfit, sans-serif', toolbar: { show: false }, background: 'transparent' },
    theme: { mode: isDark ? 'dark' : 'light' },
    dataLabels: { enabled: false },
    stroke: { curve: 'smooth', width: 3 },
    xaxis: { type: 'datetime', labels: { datetimeUTC: true, format: 'dd/MM' } },
    fill: { type: 'gradient', gradient: { shadeIntensity: 1, opacityFrom: 0.4, opacityTo: 0.05, stops: [0, 100] } },
    legend: { position: 'top', horizontalAlign: 'right' },
    tooltip: { theme: isDark ? 'dark' : 'light', x: { format: 'dd/MM/yyyy' } }
  };

  const el = document.querySelector("#activityChart");
  if (el) {
    if (window.activityChartVar) window.activityChartVar.updateOptions(options);
    else { window.activityChartVar = new ApexCharts(el, options); window.activityChartVar.render(); }
  }
}

function renderHourlyChart(hourlyData, action) {
  const hours = Array.from({ length: 24 }, (_, i) => i.toString().padStart(2, '0'));
  const counts = hours.map(h => {
    const found = hourlyData.find(d => parseInt(d.hour) == parseInt(h));
    return found ? found.count : 0;
  });

  const seriesName = action && ACTION_META[action] ? ACTION_META[action].countHeader : 'Tổng tương tác';
  const isDark = document.documentElement.getAttribute('data-theme') === 'dark';
  const options = {
    series: [{ name: seriesName, data: counts }],
    chart: { type: 'bar', height: 350, fontFamily: 'Outfit, sans-serif', toolbar: { show: false }, background: 'transparent' },
    colors: ['#6366f1'],
    theme: { mode: isDark ? 'dark' : 'light' },
    plotOptions: { bar: { borderRadius: 4, columnWidth: '60%' } },
    xaxis: { categories: hours.map(h => h + 'h') },
    tooltip: { theme: isDark ? 'dark' : 'light' }
  };

  const el = document.querySelector("#hourlyChart");
  if (el) {
    if (window.hourlyChartVar) window.hourlyChartVar.updateOptions(options);
    else { window.hourlyChartVar = new ApexCharts(el, options); window.hourlyChartVar.render(); }
  }
}

function renderDonutChart(id, stats) {
    const el = document.getElementById(id);
    if (!el) return;

    if (!stats || stats.length === 0) {
        el.innerHTML = '<div style="height:250px; display:flex; align-items:center; justify-content:center;" class="muted small text-center">Chưa có dữ liệu</div>';
        if (charts[id]) {
            charts[id].destroy();
            charts[id] = null;
        }
        return;
    }

    const labels = stats.map(s => s.label);
    const series = stats.map(s => s.count);
    const isDark = document.documentElement.getAttribute('data-theme') === 'dark';

    if (charts[id]) {
        charts[id].updateOptions({ labels, series });
        return;
    }

    const options = {
        series,
        labels,
        chart: { type: 'donut', height: 250, fontFamily: 'Outfit, sans-serif', background: 'transparent' },
        stroke: { show: false },
        legend: { 
            position: 'bottom', 
            labels: { colors: isDark ? '#94a3b8' : '#334155' } 
        },
        dataLabels: { enabled: false },
        theme: { mode: isDark ? 'dark' : 'light' },
        colors: ['#6366f1', '#10b981', '#f59e0b', '#0ea5e9', '#ef4444'],
        plotOptions: { pie: { donut: { size: '75%' } } }
    };

    charts[id] = new ApexCharts(el, options);
    charts[id].render();
}

function renderDetailedLogs(logs) {
    const container = document.getElementById('logRows');
    if (!container) return;
    if (logs.length === 0) {
        container.innerHTML = '<tr><td colspan="4" class="text-center muted" style="padding: 40px;">Không tìm thấy nhật ký tương tác phù hợp</td></tr>';
        return;
    }

    container.innerHTML = logs.map(log => {
        const time = new Date(log.createdAt);
        const vnTime = new Date(time.getTime() + (7 * 3600 * 1000));
        const timeStr = vnTime.toISOString().replace('T', ' ').substring(0, 19);
        
        const actionInfo = ACTION_META[log.action] || { label: log.action, icon: 'fa-circle-dot' };
        
        // Parse Screen Info if available
        let screenText = 'N/A';
        try {
            if (log.screenInfo) {
                const s = typeof log.screenInfo === 'string' ? JSON.parse(log.screenInfo) : log.screenInfo;
                screenText = `${s.w}x${s.h} (@${s.dpr}x)`;
            }
        } catch (e) {}

        const platformIcon = log.platform === 'app' ? '<i class="fa-solid fa-mobile-screen"></i> App' : '<i class="fa-solid fa-globe"></i> Web';
        
        // Comprehensive Device Info
        const deviceHtml = `
            <div style="display:grid; grid-template-columns: 1fr 1fr; gap:12px;">
                <div>
                    <div style="font-weight:600; color:var(--accent-color); font-size:0.9rem;">${log.os || 'Unknown OS'}</div>
                    <div class="muted small">${log.browser || 'Browser'}</div>
                    <div class="muted" style="font-size:0.7rem; margin-top:4px;">ID: ${log.deviceId || 'N/A'}</div>
                </div>
                <div style="border-left: 1px solid var(--border-color); padding-left:12px;">
                    <div class="small" style="font-weight:500;">${platformIcon}</div>
                    <div class="muted small" style="margin-top:2px;"><i class="fa-solid fa-maximize" style="font-size:0.7rem;"></i> ${screenText}</div>
                    <div class="badge small" style="margin-top:6px; background:rgba(99,102,241,0.1); color:var(--accent-color); border:none;">
                        <i class="fa-solid ${actionInfo.icon}"></i> ${actionInfo.label}
                    </div>
                </div>
            </div>
        `;

        return `
            <tr>
                <td class="small">${timeStr}</td>
                <td>
                    <div style="font-weight:600; color:var(--text-color);">${log.poiName || 'N/A'}</div>
                    <div class="muted small">ID: ${log.poiId || '-'}</div>
                </td>
                <td>${deviceHtml}</td>
                <td class="muted small">${log.ip || '-'}</td>
            </tr>
        `;
    }).join('');
}

function renderPoiRanking(topPois, action) {
  const container = document.getElementById('poiRankingRows');
  if (!container) return;
  if (action === 'online') {
    container.innerHTML = '<tr><td colspan="4" class="text-center muted">Bỏ chọn thẻ "Online" để xem xếp hạng.</td></tr>';
    return;
  }
  if (topPois.length === 0) {
    container.innerHTML = `<tr><td colspan="4" class="text-center muted">Không có dữ liệu.</td></tr>`;
    return;
  }
  const maxCount = Math.max(...topPois.map(p => p.count));
  container.innerHTML = topPois.map((poi, idx) => {
    const percentage = (poi.count / maxCount) * 100;
    const rankClass = idx === 0 ? 'rank-1' : (idx === 1 ? 'rank-2' : (idx === 2 ? 'rank-3' : 'rank-other'));
    return `
      <tr>
        <td><div class="rank-badge ${rankClass}">${idx + 1}</div></td>
        <td>
          <div style="font-weight: 600; color: var(--text-color);">${poi.name}</div>
          <div style="font-size: 0.75rem; color: var(--text-muted);">ID: ${poi.poiId}</div>
        </td>
        <td class="text-right"><strong style="color: var(--text-color);">${poi.count.toLocaleString()}</strong></td>
        <td><div class="progress-bar-wrap"><div class="progress-bar-fill" style="width: ${percentage}%;"></div></div></td>
      </tr>
    `;
  }).join('');
}

async function exportToCsv() {
    try {
        const params = new URLSearchParams();
        params.set('pageSize', 200); // Export more
        const res = await fetch('/api/admin/reports/user-activities?' + params.toString(), {
            headers: { 'Authorization': 'Bearer ' + token }
        });
        if (!res.ok) return;
        const data = await res.json();
        const logs = data.recentLogs || [];
        
        const headers = ["ID", "Time", "Action", "POI", "Device", "OS", "IP", "Screen"];
        const rows = logs.map(l => [l.id, l.createdAt, l.action, l.poiName, l.browser, l.os, l.ip, l.screenInfo]);
        
        let csvContent = "\uFEFF" + headers.join(",") + "\n" + rows.map(r => r.join(",")).join("\n");
        const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' });
        const url = URL.createObjectURL(blob);
        const link = document.createElement("a");
        link.setAttribute("href", url);
        link.setAttribute("download", `foodstreet-logs-${new Date().toISOString().split('T')[0]}.csv`);
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
    } catch(e) {}
}

loadDashboard();

setInterval(async () => {
    try {
        const res = await fetch('/api/admin/reports/user-activities?fields=onlineNow', { headers: { 'Authorization': 'Bearer ' + token } });
        if (res.ok) {
            const data = await res.json();
            const el = document.getElementById('valOnlineNow');
            if (el) el.textContent = data.onlineNow || 0;
        }
    } catch(e) {}
}, 30000);
