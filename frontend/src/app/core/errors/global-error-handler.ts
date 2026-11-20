import { ErrorHandler, inject, Injectable, Injector } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ClientLogService } from '../../services/client-log.service';

// Tiempo que el aviso queda visible antes de cerrarse solo.
const noticeDurationMs = 6000;

/**
 * Errores del navegador que nadie manejó (por ejemplo, un fallo en un componente).
 * Los registra en la consola, los envía a la API para que queden en los logs del backend
 * y avisa al usuario con un mensaje propio.
 */
@Injectable()
export class GlobalErrorHandler implements ErrorHandler {
  private snackBar = inject(MatSnackBar);
  // ClientLogService usa HttpClient; se obtiene al momento del error y no al crear el handler,
  // porque Angular crea el ErrorHandler muy temprano y pedirlo antes puede formar dependencias circulares.
  private injector = inject(Injector);

  handleError(error: unknown) {
    console.error('[App] Error no controlado:', error);
    this.injector.get(ClientLogService).reportError(error);
    this.snackBar.open('Ocurrió un error inesperado. Si persiste, recarga la página.', 'Cerrar', {
      duration: noticeDurationMs,
    });
  }
}
