import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../environments/environment';
import { AuthService } from './auth.service';

// Mismos límites que ClientErrorReport en la API: se recorta antes de enviar para no ser rechazado.
const messageMaxLength = 1000;
const stackMaxLength = 4000;
const routeMaxLength = 500;

// Tope de reportes por carga de página: un error que se repite en cada render no inunda la API.
const maxReportsPerPage = 10;

/** Envía a la API los errores del navegador para que queden en los logs del backend. */
@Injectable({ providedIn: 'root' })
export class ClientLogService {
  private http = inject(HttpClient);
  private authService = inject(AuthService);
  private url = `${environment.apiUrl}/client-logs`;
  private sentReports = 0;

  reportError(error: unknown) {
    // El endpoint exige sesión: sin ella (por ejemplo en el login) el error queda solo en la consola.
    const hasSession = this.authService.session !== null;
    const reachedLimit = this.sentReports >= maxReportsPerPage;
    if (!hasSession || reachedLimit) return;

    this.sentReports++;
    const report = {
      message: errorText(error).slice(0, messageMaxLength),
      stack: error instanceof Error ? error.stack?.slice(0, stackMaxLength) : undefined,
      route: location.pathname.slice(0, routeMaxLength),
    };
    // Si el envío falla se ignora: reportar ese fallo podría generar un ciclo de errores.
    this.http.post(this.url, report).subscribe({ error: () => {} });
  }
}

// Un error puede ser cualquier valor lanzado con `throw`, no solo un Error.
function errorText(error: unknown): string {
  if (error instanceof Error) return error.message || error.name;
  return String(error);
}
