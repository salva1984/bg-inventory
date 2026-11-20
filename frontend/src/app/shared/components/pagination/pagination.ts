import { Component, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';

/** Pie de tabla: total de resultados y botones para cambiar de página. */
@Component({
  selector: 'app-pagination',
  imports: [MatButtonModule],
  templateUrl: './pagination.html',
  styleUrl: './pagination.css',
})
export class Pagination {
  page = input.required<number>();
  totalPages = input.required<number>();
  totalCount = input.required<number>();
  // Emite la página a la que se quiere ir.
  pageChange = output<number>();
}
