import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../environments/environment';
import { Session } from '../interfaces/auth';

const sessionKey = 'inventory-session';

/** Inicia y cierra la sesión. La sesión vive en `sessionStorage`, solo mientras dure la pestaña. */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private http = inject(HttpClient);
  private url = `${environment.apiUrl}/auth`;

  session: Session | null = this.readSession();
  // Verdadero cuando la API rechazó el token; la pantalla de login lo muestra como aviso.
  sessionExpired = false;

  get isAdmin() {
    return this.session?.user.role === 'Admin';
  }

  login(email: string, password: string) {
    return this.http.post<Session>(`${this.url}/login`, { email, password });
  }

  startSession(session: Session) {
    this.session = session;
    this.sessionExpired = false;
    sessionStorage.setItem(sessionKey, JSON.stringify(session));
  }

  logout() {
    this.session = null;
    sessionStorage.removeItem(sessionKey);
  }

  expireSession() {
    this.logout();
    this.sessionExpired = true;
  }

  // Recupera la sesión guardada si todavía no venció.
  private readSession(): Session | null {
    try {
      const stored = sessionStorage.getItem(sessionKey);
      if (!stored) return null;

      const session: Session = JSON.parse(stored);
      const isExpired = new Date(session.expiresAt).getTime() <= Date.now();
      return isExpired ? null : session;
    } catch {
      return null;
    }
  }
}
