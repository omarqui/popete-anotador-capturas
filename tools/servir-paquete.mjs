// Sirve el .msix y novedades.txt a la pagina de Partner Center, para que Claude in Chrome los
// suba sin que nadie arrastre nada (skill publicar-store, nota 46).
//
// La extension no puede subir el paquete (file_upload admite hasta 10 MB), pero la pagina si
// puede descargarlo de 127.0.0.1 con fetch y ponerlo en su <input type=file>. Por eso este
// servidor responde con CORS solo para partner.microsoft.com y con el permiso de red privada.
// La primera vez, Chrome pregunta si partner.microsoft.com puede acceder a la red local: hasta
// que el usuario lo permite, la pestana no responde.
//
// Uso (desde la raiz del repo, en segundo plano):
//   node tools/servir-paquete.mjs artifacts/msix/Popete_1.2.1.0_x64.msix
//
//   http://127.0.0.1:8765/paquete.msix   el paquete (el nombre real va en /info)
//   http://127.0.0.1:8765/novedades.txt  novedades.txt de la raiz del repo
//   http://127.0.0.1:8765/info           { nombre, bytes, version }
//
// Se cierra solo despues de servir el paquete y las novedades, o a los 30 minutos.
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ORIGIN = 'https://partner.microsoft.com';
const PORT = 8765;
const LIMIT_MS = 30 * 60 * 1000;

const msix = process.argv[2];
if (!msix || !fs.existsSync(msix)) {
  console.error('Uso: node tools/servir-paquete.mjs <ruta del .msix>');
  process.exit(1);
}
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const notesPath = path.join(root, 'novedades.txt');
const name = path.basename(msix);
const bytes = fs.statSync(msix).size;
const version = /_(\d+\.\d+\.\d+\.\d+)_/.exec(name)?.[1] ?? null;

const served = new Set();
function done(what) {
  served.add(what);
  if (served.has('paquete') && served.has('novedades')) {
    console.log('Paquete y novedades servidos: listo.');
    setTimeout(() => process.exit(0), 1000);
  }
}

const server = http.createServer((req, res) => {
  res.setHeader('Access-Control-Allow-Origin', ORIGIN);
  res.setHeader('Access-Control-Allow-Private-Network', 'true');
  res.setHeader('Access-Control-Allow-Methods', 'GET');
  res.setHeader('Access-Control-Allow-Headers', '*');
  if (req.method === 'OPTIONS') { res.writeHead(204); return res.end(); }
  console.log(new Date().toISOString(), req.method, req.url, req.headers.origin ?? '');

  if (req.url === '/info') {
    res.writeHead(200, { 'Content-Type': 'application/json' });
    return res.end(JSON.stringify({ nombre: name, bytes, version }));
  }
  if (req.url === '/novedades.txt') {
    const text = fs.readFileSync(notesPath, 'utf8').replace(/^﻿/, '').trim();
    res.writeHead(200, { 'Content-Type': 'text/plain; charset=utf-8' });
    res.end(text);
    return done('novedades');
  }
  if (req.url === '/paquete.msix') {
    res.writeHead(200, { 'Content-Type': 'application/octet-stream', 'Content-Length': bytes });
    const stream = fs.createReadStream(msix);
    stream.on('end', () => done('paquete'));
    return stream.pipe(res);
  }
  res.writeHead(404);
  res.end();
});

server.listen(PORT, '127.0.0.1', () => console.log(`Sirviendo ${name} (${bytes} bytes) en http://127.0.0.1:${PORT}`));
setTimeout(() => { console.log('30 minutos sin terminar: se cierra.'); process.exit(0); }, LIMIT_MS).unref();
