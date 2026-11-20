import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, provideZonelessChangeDetection } from '@angular/core';
import { MAT_FORM_FIELD_DEFAULT_OPTIONS, MatFormFieldDefaultOptions } from '@angular/material/form-field';
import { MAT_SELECT_CONFIG, MatSelectConfig } from '@angular/material/select';
import { provideRouter } from '@angular/router';
import { authInterceptor } from './core/interceptors/auth.interceptor';
import { routes } from './app.routes';

// Todos los campos son con borde y sin el hueco fijo que Material reserva debajo
// para errores; el espacio aparece solo cuando hay un mensaje que mostrar.
const formFieldDefaults: MatFormFieldDefaultOptions = {
  appearance: 'outline',
  subscriptSizing: 'dynamic',
};

// La lista desplegable crece hasta el texto de la opción más larga en lugar de copiar el
// ancho del campo; así los nombres largos de proveedor se leen completos.
const selectDefaults: MatSelectConfig = {
  panelWidth: null,
};

export const appConfig: ApplicationConfig = {
  providers: [
    // Sin zone.js: la pantalla se actualiza cuando cambia un signal o hay un evento del template.
    provideZonelessChangeDetection(),
    provideRouter(routes),
    provideHttpClient(withInterceptors([authInterceptor])),
    { provide: MAT_FORM_FIELD_DEFAULT_OPTIONS, useValue: formFieldDefaults },
    { provide: MAT_SELECT_CONFIG, useValue: selectDefaults },
  ],
};
