import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';

// Status que el navegador da a una petición que no obtuvo respuesta: API caída, sin red o bloqueada por CORS.
const noResponseStatus = 0;

// Las respuestas de error (4xx/5xx) ya quedan en los logs del backend (request logging de Serilog).
// Aquí solo se registran las peticiones que nunca llegaron a la API, que el backend no puede ver.
export const errorLogInterceptor: HttpInterceptorFn = (request, next) =>
  next(request).pipe(catchError(error => {
    const gotNoResponse = error instanceof HttpErrorResponse && error.status === noResponseStatus;
    if (gotNoResponse) console.error(`[API] ${request.method} ${request.url} sin respuesta`, error);
    return throwError(() => error);
  }));
