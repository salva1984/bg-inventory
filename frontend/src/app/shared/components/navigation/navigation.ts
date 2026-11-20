import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';

/** Enlaces de las secciones principales; la sección actual se marca con `routerLinkActive`. */
@Component({
  selector: 'app-navigation',
  imports: [RouterLink, RouterLinkActive],
  templateUrl: './navigation.html',
  styleUrl: './navigation.css',
})
export class Navigation {}
