import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

/** Raíz de la aplicación: solo aloja la pantalla activa del router. */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  templateUrl: './app.html',
})
export class App {}
