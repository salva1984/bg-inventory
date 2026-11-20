import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from '../../services/auth.service';

/**
 * Agrega el token a cada petición y, si la API responde 401 con una sesión activa,
 * la da por vencida: cierra los diálogos y lleva al login.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const authService = inject(AuthService);
  const router = inject(Router);
  const dialog = inject(MatDialog);

  const token = authService.session?.accessToken;
  // Sin sesión (por ejemplo en el login) la petición sale tal cual.
  let authorizedRequest = request;
  if (token) {
    authorizedRequest = request.clone(
      { setHeaders: { Authorization: `Bearer ${token}` } 
    });
  }

  return next(authorizedRequest).pipe(catchError(error => {
    // Sin sesión, un 401 es solo un login fallido y lo maneja la pantalla de login.
    if (error instanceof HttpErrorResponse && error.status === 401 && authService.session) {
      authService.expireSession();
      dialog.closeAll();
      router.navigateByUrl('/login');
    }
    return throwError(() => error);
  }));
};
