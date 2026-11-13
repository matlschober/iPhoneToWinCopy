'use strict';
(() => {
  const $ = id => document.getElementById(id);
  const supplied = location.hash.slice(1);
  if (/^[a-f0-9]{48}$/i.test(supplied)) {
    try { localStorage.setItem('pocket-key', supplied); } catch { /* Private storage may be unavailable. */ }
  }
  let stored = '';
  try { stored = localStorage.getItem('pocket-key') || ''; } catch { }
  const key = /^[a-f0-9]{48}$/i.test(supplied) ? supplied : stored;
  history.replaceState(null, '', location.pathname);
  let mode = 'photo', online = false, busy = false, urls = [], selectedFiles = [];
  let limits = null;
  let remainingBytes = 0;
  const sizeLabel = bytes => bytes >= 1073741824 ? (bytes / 1073741824).toLocaleString('de', { maximumFractionDigits: 1 }) + ' GB' : bytes > 0 && bytes < 104857.6 ? '< 0,1 MB' : (bytes / 1048576).toLocaleString('de', { maximumFractionDigits: 1 }) + ' MB';
  function showMessage(text, error = false) {
    $('message').textContent = text;
    $('message').className = error ? 'error' : '';
    $('message').hidden = false;
  }
  function updateButton() {
    $('send').disabled = busy || !online || !(mode === 'photo' ? selectedFiles.length : $('text').value.trim().length);
    $('send-label').textContent = busy ? 'Wird gesendet …' : 'An PC senden';
  }
  function setMode(next) {
    if (busy) return;
    mode = next;
    for (const tab of ['photo', 'text']) {
      $(tab + '-tab').setAttribute('aria-selected', String(tab === mode));
      $(tab + '-tab').tabIndex = tab === mode ? 0 : -1;
      $(tab + '-panel').hidden = tab !== mode;
    }
    $('message').hidden = true;
    updateButton();
  }
  for (const tab of ['photo', 'text']) {
    $(tab + '-tab').addEventListener('click', () => setMode(tab));
    $(tab + '-tab').addEventListener('keydown', event => {
      if (['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) {
        event.preventDefault(); const next = mode === 'photo' ? 'text' : 'photo';
        setMode(next); $(next + '-tab').focus();
      }
    });
  }
  function renderFiles() {
    urls.forEach(URL.revokeObjectURL); urls = [];
    $('previews').replaceChildren();
    const files = selectedFiles;
    $('selection').hidden = !files.length;
    $('selection-count').textContent = files.length + (files.length === 1 ? ' Foto ausgewählt' : ' Fotos ausgewählt');
    for (const file of files) {
      const img = document.createElement('img');
      img.alt = file.name;
      const url = URL.createObjectURL(file); urls.push(url); img.src = url;
      img.onerror = () => {
        const placeholder = document.createElement('span'); placeholder.className = 'file-placeholder'; placeholder.textContent = file.name;
        img.replaceWith(placeholder);
      };
      $('previews').append(img);
    }
    $('message').hidden = true;
    updateButton();
  }
  function validFiles(files) {
    if (!limits) { showMessage('Bitte warten, bis die Limits vom PC geladen sind.', true); return false; }
    const total = files.reduce((n, f) => n + f.size, 0);
    if (total > remainingBytes) {
      showMessage('Zu wenig Platz im Eingang: Die Fotos benötigen ' + sizeLabel(total) + ', verfügbar sind noch ' + sizeLabel(remainingBytes) + '. Bitte am PC Einträge löschen oder den Empfangsspeicher erhöhen.', true);
      return false;
    }
    return true;
  }
  $('files').addEventListener('change', () => {
    const files = Array.from($('files').files);
    if (files.length && validFiles(files)) { selectedFiles = files; renderFiles(); }
    // Keep the selection in memory so choosing the same file again fires change.
    $('files').value = '';
  });
  $('clear-files').addEventListener('click', () => { if (!busy) { selectedFiles = []; $('files').value = ''; renderFiles(); } });
  document.addEventListener('paste', event => {
    if (mode !== 'photo') return;
    const inPasteField = $('paste-target').contains(event.target);
    const clipboard = event.clipboardData;
    let images = Array.from(clipboard?.files || []).filter(file => file.type.startsWith('image/'));
    if (!images.length) images = Array.from(clipboard?.items || [])
      .filter(item => item.kind === 'file' && item.type.startsWith('image/'))
      .map(item => item.getAsFile()).filter(Boolean);
    if (!images.length && !inPasteField) return;
    // Never insert clipboard HTML, remote image URLs or text into the editable field.
    event.preventDefault();
    if (busy) { showMessage('Bitte warten, bis die laufende Übertragung fertig ist.', true); return; }
    if (!images.length) {
      showMessage('Kein Bild in der Zwischenablage gefunden. Bitte das Foto selbst kopieren, nicht nur den Link, oder „Fotos auswählen“ verwenden.', true);
      return;
    }
    const files = images.map((file, index) => {
      if (file.name && file.name !== 'image.png') return file;
      const extension = ({ 'image/jpeg': 'jpg', 'image/png': 'png', 'image/heic': 'heic', 'image/heif': 'heif', 'image/webp': 'webp', 'image/gif': 'gif', 'image/tiff': 'tif', 'image/bmp': 'bmp' })[file.type] || 'image';
      return new File([file], 'Eingefuegtes-Foto-' + Date.now() + '-' + (index + 1) + '.' + extension, { type: file.type });
    });
    if (!validFiles([...selectedFiles, ...files])) return;
    selectedFiles.push(...files);
    renderFiles();
    $('paste-target').replaceChildren();
    $('paste-target').blur();
    showMessage(files.length === 1 ? 'Bild eingefügt. Jetzt kannst du es an den PC senden.' : files.length + ' Bilder eingefügt. Jetzt kannst du sie an den PC senden.');
  });
  $('paste-target').addEventListener('input', () => $('paste-target').replaceChildren());
  $('text').addEventListener('input', () => { $('char-count').textContent = $('text').value.length.toLocaleString('de') + ' Zeichen'; updateButton(); });
  async function checkConnection() {
    if (!key) {
      $('connection').textContent = 'Noch nicht verbunden'; $('pairing-help').hidden = false; return;
    }
    try {
      const response = await fetch('/api/status', { headers: { 'X-Pocket-Key': key }, signal: AbortSignal.timeout(5000) });
      const data = await response.json();
      if (!response.ok) throw new Error(data.error || 'Nicht verbunden');
      limits = data.limits;
      remainingBytes = data.remainingBytes ?? 0;
      online = !data.paused && !!limits;
      if (limits) {
        $('limits-hint').textContent = sizeLabel(remainingBytes) + ' von ' + sizeLabel(limits.maxInboxBytes) + ' Empfangsspeicher frei';
      }
      $('connection').textContent = data.paused ? 'Empfang am PC pausiert' : 'Verbunden mit ' + data.name;
      $('connection').parentElement.classList.toggle('connected', online);
      $('pairing-help').hidden = true;
    } catch (error) {
      online = false;
      $('connection').textContent = 'PC nicht erreichbar oder Verbindung abgelaufen';
      $('connection').parentElement.classList.remove('connected');
      $('pairing-help').hidden = false;
    }
    updateButton();
  }
  $('send-form').addEventListener('submit', event => {
    event.preventDefault();
    if (busy || !online) return;
    const body = new FormData();
    if (mode === 'photo') {
      const files = selectedFiles;
      if (!files.length) return;
      if (!validFiles(files)) return;
      files.forEach(file => body.append('files', file));
    } else {
      if (!$('text').value.trim()) return;
      const bytes = new TextEncoder().encode($('text').value).length;
      if (bytes > remainingBytes) {
        showMessage('Zu wenig Platz im Eingang: Der Text benötigt ' + sizeLabel(bytes) + ', verfügbar sind noch ' + sizeLabel(remainingBytes) + '.', true); return;
      }
      body.append('text', $('text').value);
    }
    busy = true; updateButton();
    $('files').disabled = true; $('text').readOnly = true;
    $('paste-target').contentEditable = 'false'; $('paste-target').setAttribute('aria-disabled', 'true');
    $('message').hidden = true; $('progress').hidden = false;
    $('progress').querySelector('progress').value = 0;
    const xhr = new XMLHttpRequest();
    xhr.open('POST', '/api/receive'); xhr.timeout = 180000; xhr.setRequestHeader('X-Pocket-Key', key);
    xhr.upload.onprogress = event => {
      if (event.lengthComputable) {
        const percent = Math.round(event.loaded / event.total * 100);
        $('progress').querySelector('progress').value = percent;
        $('progress-label').textContent = percent === 100 ? 'Wird am PC gespeichert …' : percent + ' % übertragen';
      }
    };
    xhr.onload = () => {
      let data; try { data = JSON.parse(xhr.responseText); } catch { data = { error: 'Unerwartete Antwort vom PC.' }; }
      if (xhr.status >= 200 && xhr.status < 300 && !data.error) {
        if (mode === 'photo') { selectedFiles = []; $('files').value = ''; renderFiles(); }
        else { $('text').value = ''; $('char-count').textContent = '0 Zeichen'; }
        showMessage('✓ Am PC angekommen. ' + data.received + (data.received === 1 ? ' Eintrag ist' : ' Einträge sind') + ' jetzt in deinem Eingang.');
      } else showMessage(data.error || 'Senden fehlgeschlagen. Bitte erneut versuchen.', true);
    };
    xhr.onerror = xhr.ontimeout = () => showMessage('Verbindung unterbrochen. Prüfe am PC, ob etwas angekommen ist, bevor du erneut sendest.', true);
    xhr.onloadend = () => {
      busy = false; $('files').disabled = false; $('text').readOnly = false; $('progress').hidden = true; updateButton(); checkConnection();
      $('paste-target').contentEditable = 'true'; $('paste-target').removeAttribute('aria-disabled');
    };
    xhr.send(body);
  });
  checkConnection();
  setInterval(() => { if (!busy && !document.hidden) checkConnection(); }, 10000);
  document.addEventListener('visibilitychange', () => { if (!document.hidden && !busy) checkConnection(); });
})();
