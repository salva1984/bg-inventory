export type Role = 'Admin' | 'Viewer';

export interface User {
  id: number;
  email: string;
  fullName: string;
  role: Role;
}

// Respuesta del login; se guarda tal cual en la pestaña.
export interface Session {
  accessToken: string;
  expiresAt: string;
  user: User;
}
