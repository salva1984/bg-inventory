import { HttpErrorResponse } from '@angular/common/http';

/** Convierte un error HTTP en un mensaje legible para el usuario. */
export function errorMessage(error: unknown): string {
  if (!(error instanceof HttpErrorResponse)) return 'Ocurrió un error inesperado.';
  if (error.status === 0) return 'No se pudo conectar con la API. Comprueba que esté funcionando.';
  if (error.status === 429) return 'Demasiados intentos. Espera un minuto e inténtalo de nuevo.';

  // Errores de validación: la API devuelve un mensaje por campo.
  const details = error.error;
  if (details?.errors) return Object.values(details.errors).flat().join(' ');

  const apiMessage = details?.detail || details?.title;
  if (apiMessage) return apiMessage;

  if (error.status === 401) return 'La sesión ha expirado o las credenciales son incorrectas.';
  return 'No se pudo completar la operación.';
}
