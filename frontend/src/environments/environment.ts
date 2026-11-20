// Entorno de producción (configuración por defecto de `ng build`).
// __API_URL__ es un marcador: el pipeline de CI lo reemplaza antes de compilar, por ejemplo
//   sed -i "s|__API_URL__|$API_URL|" src/environments/environment.ts
// Si se olvida, las peticiones fallan de forma evidente en vez de apuntar a una URL equivocada.
export const environment = {
  apiUrl: '__API_URL__',
};
