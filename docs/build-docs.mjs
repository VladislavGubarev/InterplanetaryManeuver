import fs from 'node:fs';
import { marked } from 'marked';

const root = process.argv[2];
const mdPath = `${root}/interplanetary_maneuver_documentation.md`;
const fragmentPath = `${root}/interplanetary_maneuver_documentation.fragment.html`;
const htmlPath = `${root}/interplanetary_maneuver_documentation.html`;

const markdown = fs.readFileSync(mdPath, 'utf8').replace(/^\uFEFF/, '');
const body = marked.parse(markdown, { gfm: true });
const html = `<!doctype html>
<html lang="ru">
<head>
  <meta charset="utf-8" />
  <title>Межпланетный манёвр — техническая документация</title>
  <link rel="stylesheet" href="documentation.css" />
</head>
<body>
  <main>
${body}
  </main>
</body>
</html>
`;

fs.writeFileSync(fragmentPath, body, 'utf8');
fs.writeFileSync(htmlPath, html, 'utf8');
