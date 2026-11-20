// Genera src/environments/environment.ts a partir de variables de entorno.
// Se ejecuta antes de `npm start` y `npm run build` (ver "prestart" y "prebuild" en package.json).
// Angular compila la URL dentro del bundle, así que la variable debe estar definida al construir.
import { writeFileSync } from 'node:fs';

// URL base de la API; si la variable no existe se usa la de desarrollo local.
const defaultApiUrl = 'http://localhost:8080/api';
const apiUrl = process.env.API_URL ?? defaultApiUrl;

const content = `// Archivo generado por scripts/set-env.mjs desde la variable de entorno API_URL. No editar.
export const environment = {
  apiUrl: ${JSON.stringify(apiUrl)},
};
`;

writeFileSync(new URL('../src/environments/environment.ts', import.meta.url), content);
console.log(`environment.ts generado con apiUrl=${apiUrl}`);
