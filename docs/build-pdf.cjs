const path = require('path');
const { chromium } = require('playwright-core');

(async () => {
  const root = process.argv[2];
  const htmlPath = path.join(root, 'interplanetary_maneuver_documentation.html');
  const pdfPath = path.join(root, 'interplanetary_maneuver_documentation_clean.pdf');
  const browser = await chromium.launch({
    executablePath: 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
    headless: true
  });
  const page = await browser.newPage();
  await page.goto('file:///' + htmlPath.replace(/\\/g, '/'), { waitUntil: 'networkidle' });
  await page.pdf({
    path: pdfPath,
    format: 'A4',
    printBackground: true,
    displayHeaderFooter: false,
    margin: { top: '14mm', right: '12mm', bottom: '14mm', left: '12mm' }
  });
  await browser.close();
})();
