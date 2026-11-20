import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { AuthService } from '../../../services/auth.service';

/**
 * Enlaces de las secciones principales; la sección actual se marca con `routerLinkActive`.
 * Recepción e historial de facturas solo aparecen para administradores, los únicos que pueden usarlos.
 */
@Component({
  selector: 'app-navigation',
  imports: [RouterLink, RouterLinkActive],
  templateUrl: './navigation.html',
  styleUrl: './navigation.css',
})
export class Navigation {
  isAdmin = inject(AuthService).isAdmin;
}
