import { Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router } from '@angular/router';
import { AuthService } from '../../../services/auth.service';

/** Cuenta del usuario con sesión: iniciales, nombre, rol y botón para salir. */
@Component({
  selector: 'app-account',
  imports: [MatButtonModule, MatIconModule, MatTooltipModule],
  templateUrl: './account.html',
  styleUrl: './account.css',
})
export class Account {
  private authService = inject(AuthService);
  private router = inject(Router);
  private dialog = inject(MatDialog);

  fullName = this.authService.session?.user.fullName ?? '';
  isAdmin = this.authService.isAdmin;
  roleLabel = this.isAdmin ? 'Administrador' : 'Solo lectura';
  initials = this.toInitials(this.fullName);

  logout() {
    this.dialog.closeAll();
    this.authService.logout();
    this.router.navigateByUrl('/login');
  }

  // Primera letra de las dos primeras palabras del nombre.
  private toInitials(fullName: string) {
    const words = fullName.split(/\s+/).filter(Boolean);
    const firstTwoWords = words.slice(0, 2);
    return firstTwoWords.map(word => word[0]).join('').toUpperCase();
  }
}
