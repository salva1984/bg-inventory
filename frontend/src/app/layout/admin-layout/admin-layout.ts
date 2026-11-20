import { Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../../services/auth.service';
import { Brand } from '../../shared/components/brand/brand';

/** Marco de las pantallas internas: barra lateral con navegación y cuenta, y el contenido a su derecha. */
@Component({
  selector: 'app-admin-layout',
  imports: [Brand, RouterLink, RouterLinkActive, RouterOutlet, MatButtonModule, MatIconModule, MatTooltipModule],
  templateUrl: './admin-layout.html',
  styleUrl: './admin-layout.css',
})
export class AdminLayout {
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
