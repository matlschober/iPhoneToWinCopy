// Uses the local QA installation, or a caller-provided Playwright module.
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || '../.tools/browser/node_modules/playwright');
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const web = path.join(__dirname, '../src/PocketBridge.Core/Web');
const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j2ioAAAAASUVORK5CYII=', 'base64');

(async () => {
  const channel = process.env.PLAYWRIGHT_BROWSER_CHANNEL || (process.platform === 'win32' ? 'msedge' : undefined);
  const browser = await chromium.launch({ channel, headless: true });
  try {
    const page = await browser.newPage({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true, reducedMotion: 'reduce' });
    const errors = [], uploads = [];
    let rejectUpload = false;
    let limits = { maxInboxMb: 512, maxInboxBytes: 512 * 1048576 };
    let remainingBytes = limits.maxInboxBytes;
    page.on('pageerror', error => errors.push(error.message));
    await page.route('http://192.0.2.1:47831/**', async route => {
      const request = route.request();
      const pathname = new URL(request.url()).pathname;
      if (pathname === '/api/status') return route.fulfill({ json: { name: 'Test-PC', paused: false, limits, remainingBytes } });
      if (pathname === '/api/receive') {
        const form = await new Request(request.url(), { method: 'POST', headers: request.headers(), body: request.postDataBuffer() }).formData();
        uploads.push(form);
        return route.fulfill({ status: rejectUpload ? 503 : 200, json: rejectUpload ? { error: 'Empfang pausiert' } : { received: Array.from(form.keys()).length } });
      }
      const file = pathname === '/' ? 'index.html' : pathname.slice(1);
      assert.ok(['index.html', 'app.js', 'app.css'].includes(file));
      await route.fulfill({ body: fs.readFileSync(path.join(web, file)), contentType: file.endsWith('.js') ? 'text/javascript' : file.endsWith('.css') ? 'text/css' : 'text/html' });
    });
    await page.goto('http://192.0.2.1:47831/#' + 'a'.repeat(48));
    await page.waitForFunction(() => document.querySelector('#connection').textContent.startsWith('Verbunden mit'));
    assert.equal(await page.evaluate(() => isSecureContext), false, 'Test uses ordinary HTTP');
    const paste = async (imageCount = 1, text = '') => page.evaluate(({ bytes, imageCount, text }) => {
      const clipboard = new DataTransfer();
      for (let i = 0; i < imageCount; i++) clipboard.items.add(new File([new Uint8Array(bytes)], 'image.png', { type: 'image/png' }));
      if (text) clipboard.setData('text/html', text);
      document.querySelector('#paste-target').dispatchEvent(new ClipboardEvent('paste', { bubbles: true, cancelable: true, clipboardData: clipboard }));
    }, { bytes: [...png], imageCount, text });
    await paste();
    await page.getByText('1 Foto ausgewählt', { exact: true }).waitFor();
    assert.equal(uploads.length, 0, 'Paste does not send automatically');
    assert.equal(await page.locator('#paste-target').innerHTML(), '', 'Editable field stays empty');
    await page.getByRole('button', { name: 'An PC senden' }).click();
    await page.getByRole('status').filter({ hasText: 'Am PC angekommen' }).waitFor();
    const received = uploads.at(-1).get('files');
    assert.deepEqual(Buffer.from(await received.arrayBuffer()), png, 'Pasted image bytes preserved');
    assert.match(received.name, /^Eingefuegtes-Foto-.*\.png$/);
    assert.equal(await page.locator('#selection').isHidden(), true, 'Success clears selection');
    await page.locator('#files').setInputFiles({ name: 'picked.png', mimeType: 'image/png', buffer: png });
    await paste(2);
    await page.getByText('3 Fotos ausgewählt', { exact: true }).waitFor();
    rejectUpload = true;
    await page.getByRole('button', { name: 'An PC senden' }).click();
    await page.getByRole('status').filter({ hasText: 'Empfang pausiert' }).waitFor();
    assert.equal(await page.locator('#selection-count').textContent(), '3 Fotos ausgewählt', 'Failed send retains images');
    await page.waitForFunction(() => document.querySelector('#paste-target').contentEditable === 'true');
    await page.getByRole('button', { name: 'Auswahl leeren' }).click();
    await paste(0, '<img src="https://example.com/tracker.png" onerror="alert(1)">');
    await page.getByRole('status').filter({ hasText: 'Kein Bild in der Zwischenablage' }).waitFor();
    assert.equal(await page.locator('#paste-target').innerHTML(), '', 'Clipboard HTML is not inserted');
    await paste(21);
    await page.getByText('21 Fotos ausgewählt', { exact: true }).waitFor();
    assert.equal(await page.locator('#previews img').count(), 21, 'Photo count is not artificially limited');
    await page.getByRole('button', { name: 'Auswahl leeren' }).click();
    rejectUpload = false;
    await page.getByRole('tab', { name: 'Text & Links' }).click();
    await page.locator('#text').fill('Text bleibt erhalten 👋');
    await page.getByRole('button', { name: 'An PC senden' }).click();
    await page.getByRole('status').filter({ hasText: 'Am PC angekommen' }).waitFor();
    assert.equal(uploads.at(-1).get('text'), 'Text bleibt erhalten 👋', 'Text upload unchanged');
    limits = { maxInboxMb: 1, maxInboxBytes: 1048576 };
    remainingBytes = 100;
    await page.reload();
    await page.waitForFunction(() => document.querySelector('#connection').textContent.startsWith('Verbunden mit'));
    await page.getByText('< 0,1 MB von 1 MB Empfangsspeicher frei', { exact: true }).waitFor();
    await paste(2);
    await page.getByRole('status').filter({ hasText: 'Zu wenig Platz im Eingang' }).waitFor();
    await page.locator('#files').setInputFiles({ name: 'large.png', mimeType: 'image/png', buffer: Buffer.alloc(1048577) });
    await page.getByRole('status').filter({ hasText: 'Zu wenig Platz im Eingang' }).waitFor();
    await page.getByRole('tab', { name: 'Text & Links' }).click();
    await page.locator('#text').fill('€'.repeat(34));
    const count = uploads.length;
    await page.getByRole('button', { name: 'An PC senden' }).click();
    await page.getByRole('status').filter({ hasText: 'Zu wenig Platz im Eingang' }).waitFor();
    assert.equal(uploads.length, count, 'Text quota counts UTF-8 bytes locally');
    remainingBytes = limits.maxInboxBytes;
    await page.reload();
    await page.waitForFunction(() => document.querySelector('#connection').textContent.startsWith('Verbunden mit'));
    await paste(21);
    await page.getByText('21 Fotos ausgewählt', { exact: true }).waitFor();
    assert.equal(await page.locator('#previews img').count(), 21, 'Raised limit shows every image preview');
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false, 'Mobile layout fits');
    assert.deepEqual(errors, [], 'No JavaScript errors');
    console.log('PASS: HTTP paste, preview, explicit send, image bytes, mixed selection, retry, clear, HTML rejection, reception quota, UTF-8 text and mobile layout.');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
