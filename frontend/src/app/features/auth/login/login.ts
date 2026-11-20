import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { Router } from '@angular/router';
import { AuthService } from '../../../services/auth.service';
import { Brand } from '../../../shared/components/brand/brand';
import { errorMessage } from '../../../shared/utils/error-message';

/** Pantalla de inicio de sesión. */
@Component({
  selector: 'app-login',
  imports: [Brand, FormsModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  templateUrl: './login.html',
  styleUrl: './login.css',
})
export class Login {
  private authService = inject(AuthService);
  private router = inject(Router);

  email = signal('');
  password = signal('');
  busy = signal(false);
  // Si el interceptor venció la sesión, se avisa al llegar aquí.
  error = signal(this.authService.sessionExpired ? 'Tu sesión ha expirado. Inicia sesión de nuevo.' : '');

  login() {
    this.busy.set(true);
    this.error.set('');
    this.authService.login(this.email(), this.password()).subscribe({
      next: session => {
        this.authService.startSession(session);
        this.router.navigateByUrl('/');
      },
      error: error => {
        this.busy.set(false);
        this.error.set(errorMessage(error));
      },
    });
  }
}
