import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { Account } from '../../shared/components/account/account';
import { Brand } from '../../shared/components/brand/brand';
import { Navigation } from '../../shared/components/navigation/navigation';

/** Marco de las pantallas internas: barra lateral con navegación y cuenta, y el contenido a su derecha. */
@Component({
  selector: 'app-admin-layout',
  imports: [Account, Brand, Navigation, RouterOutlet],
  templateUrl: './admin-layout.html',
  styleUrl: './admin-layout.css',
})
export class AdminLayout {}
