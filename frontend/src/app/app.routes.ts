import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth-guard';
import { AdminLayout } from './layout/admin-layout/admin-layout';

export const routes: Routes = [
  {
    path: 'login',
    title: 'Iniciar sesión',
    loadComponent: () => import('./features/auth/login/login').then(m => m.Login),
  },
  {
    // Todo lo que está dentro de la barra superior exige sesión.
    path: '',
    component: AdminLayout,
    canActivate: [authGuard],
    children: [
      {
        path: 'dashboard',
        title: 'Resumen',
        loadComponent: () => import('./features/dashboard/dashboard').then(m => m.Dashboard),
      },
      {
        path: 'products',
        title: 'Productos',
        loadComponent: () => import('./features/products/products').then(m => m.Products),
      },
      {
        path: 'price-matrix',
        title: 'Matriz de precios',
        loadComponent: () => import('./features/price-matrix/price-matrix').then(m => m.PriceMatrix),
      },
      {
        path: 'suppliers',
        title: 'Proveedores',
        loadComponent: () => import('./features/suppliers/suppliers').then(m => m.Suppliers),
      },
      // El resumen es la página de inicio después de iniciar sesión.
      { path: '', redirectTo: 'dashboard', pathMatch: 'full' },
    ],
  },
  { path: '**', redirectTo: '' },
];
