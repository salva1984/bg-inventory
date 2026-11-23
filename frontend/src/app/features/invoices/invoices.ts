import { CurrencyPipe, DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, OnDestroy, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { DomSanitizer } from '@angular/platform-browser';
import { RouterLink } from '@angular/router';
import {
  invoiceNumberMaxLength,
  maxInvoiceFileBytes,
  maxInvoiceFileMegabytes,
  maxPrice,
  maxStock,
  minPrice,
  notBlankPattern,
  pricePattern,
  wholeNumberPattern,
} from '../../core/constants/validation';
import {
  ConfirmInvoiceRequest,
  InvoiceAnalysis,
  LineMatchStatus,
  MatchedBy,
  ProductCandidate,
} from '../../interfaces/invoice';
import { SupplierOption } from '../../interfaces/supplier';
import { AuthService } from '../../services/auth.service';
import { InvoiceService } from '../../services/invoice.service';
import { SupplierService } from '../../services/supplier.service';
import { errorMessage } from '../../shared/utils/error-message';
import { ProductOption, ProductPicker } from './product-picker/product-picker';

// Una línea de la factura en revisión. Sus campos se editan directamente con ngModel.
interface ReviewLine {
  lineNumber: number;
  description: string;
  supplierItemCode: string | null;
  status: LineMatchStatus;
  matchedBy: MatchedBy | null;
  candidates: ProductCandidate[];
  // Una línea excluida (un flete, una línea mal leída) no se registra.
  include: boolean;
  product: ProductOption | null;
  quantity: number;
  unitPrice: number;
}

// Etiqueta y estilo de cada estado de conciliación.
const statusBadges: Record<LineMatchStatus, { label: string; style: string }> = {
  Matched: { label: 'Reconocido', style: 'badge-primary' },
  Suggested: { label: 'Sugerido', style: '' },
  NeedsReview: { label: 'Revisar', style: 'badge-warn' },
  Manual: { label: 'Identificar', style: 'badge-warn' },
};

// Explica de dónde salió un producto reconocido.
const matchedByLabels: Record<MatchedBy, string> = {
  SupplierItemCode: 'Por el código del proveedor',
  ProductCode: 'Por el código del producto',
  Description: 'Por una factura anterior',
};

// Estados en los que el sistema ya propone un producto; en los demás lo elige el usuario.
const preselectedStatuses: LineMatchStatus[] = ['Matched', 'Suggested'];

// Código con el que la API rechaza una factura ya registrada (InvoiceErrors.AlreadyRegistered).
const alreadyRegisteredCode = 'invoice.already_registered';

// Tipos que acepta la API (se comprueban por su contenido en el servidor).
const acceptedFiles = '.pdf,.png,.jpg,.jpeg,.webp,application/pdf,image/png,image/jpeg,image/webp';

/**
 * Recepción de mercadería desde una factura, en dos pasos:
 * 1. Se sube la factura (PDF o imagen) del proveedor y la API propone un producto por línea.
 * 2. El usuario revisa, corrige y confirma; recién ahí se registra el stock, el sistema
 *    recuerda cómo nombra ese proveedor a cada producto y la factura (con su archivo) queda
 *    en el historial.
 */
@Component({
  selector: 'app-invoices',
  imports: [
    CurrencyPipe,
    DatePipe,
    FormsModule,
    MatButtonModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressBarModule,
    MatSelectModule,
    ProductPicker,
    RouterLink,
  ],
  templateUrl: './invoices.html',
  styleUrl: './invoices.css',
})
export class Invoices implements OnInit, OnDestroy {
  private invoiceService = inject(InvoiceService);
  private sanitizer = inject(DomSanitizer);
  private supplierService = inject(SupplierService);

  isAdmin = inject(AuthService).isAdmin;
  suppliers = signal<SupplierOption[]>([]);

  // Paso 1: proveedor y archivo.
  supplierId = signal<number | null>(null);
  file = signal<File | null>(null);
  // Dirección local (blob:) del archivo elegido, para mostrarlo en la revisión sin volver a subirlo.
  fileUrl = signal<string | null>(null);
  // Un iframe solo acepta direcciones marcadas como seguras. Esta apunta al archivo que el propio
  // usuario eligió en su equipo, no a contenido de terceros.
  pdfUrl = computed(() => {
    const url = this.fileUrl();
    const file = this.file();
    // El tipo lo pone el navegador según la extensión; el nombre cubre los casos en que no lo reconoce.
    const isPdf = file?.type === 'application/pdf' || file?.name.toLowerCase().endsWith('.pdf');
    return url && isPdf ? this.sanitizer.bypassSecurityTrustResourceUrl(url) : null;
  });
  showOriginal = signal(false);
  analyzing = signal(false);

  // Paso 2: lo que leyó la API y la revisión del usuario.
  analysis = signal<InvoiceAnalysis | null>(null);
  invoiceNumber = signal('');
  // Los campos de cada línea se editan sobre el objeto; el signal cambia al cargar la lista.
  lines = signal<ReviewLine[]>([]);
  confirming = signal(false);
  // Si el emisor de la factura no es el proveedor elegido, el usuario debe decidir antes de registrar:
  // el stock y las equivalencias quedarían en el proveedor equivocado.
  issuerDecided = signal(true);

  error = signal('');
  notice = signal('');

  readonly acceptedFiles = acceptedFiles;
  readonly statusBadges = statusBadges;
  readonly matchedByLabels = matchedByLabels;
  readonly invoiceNumberMaxLength = invoiceNumberMaxLength;
  readonly notBlankPattern = notBlankPattern;
  readonly pricePattern = pricePattern;
  readonly wholeNumberPattern = wholeNumberPattern;
  readonly minPrice = minPrice;
  readonly maxPrice = maxPrice;
  readonly maxStock = maxStock;

  // Getters y no computed: los campos de las líneas cambian por ngModel, no por signals.
  get includedLines() {
    return this.lines().filter(line => line.include);
  }

  get unresolvedCount() {
    return this.includedLines.filter(line => !line.product).length;
  }

  // Nombre del proveedor elegido, para los avisos de la revisión.
  get chosenSupplierName() {
    const supplierId = this.analysis()?.supplierId;
    return this.suppliers().find(supplier => supplier.id === supplierId)?.name ?? '';
  }

  get canAnalyze() {
    return this.supplierId() !== null && this.file() !== null && !this.analyzing();
  }

  get invoiceTotal() {
    return this.includedLines.reduce((total, line) => total + Number(line.quantity) * Number(line.unitPrice), 0);
  }

  ngOnInit() {
    this.supplierService.options().subscribe({
      next: suppliers => this.suppliers.set(suppliers),
      error: error => this.error.set(errorMessage(error)),
    });
  }

  ngOnDestroy() {
    this.setFile(null);
  }

  onFileSelected(event: Event) {
    const input = event.target as HTMLInputElement;
    const selected = input.files?.[0] ?? null;
    this.error.set('');

    // Se avisa antes de subir: la API rechazaría el archivo después de recibirlo entero.
    const exceedsLimit = selected !== null && selected.size > maxInvoiceFileBytes;
    if (exceedsLimit) {
      this.error.set(`El archivo supera el límite de ${maxInvoiceFileMegabytes} MB.`);
      this.setFile(null);
      input.value = '';
      return;
    }

    this.setFile(selected);
  }

  // Cambia el archivo y su dirección local; la anterior se libera para no retener el archivo en memoria.
  private setFile(file: File | null) {
    const previousUrl = this.fileUrl();
    if (previousUrl) URL.revokeObjectURL(previousUrl);

    this.file.set(file);
    this.fileUrl.set(file ? URL.createObjectURL(file) : null);
  }

  analyze() {
    const supplierId = this.supplierId();
    const file = this.file();
    if (supplierId === null || file === null) return;

    this.analyzing.set(true);
    this.error.set('');
    this.notice.set('');

    this.invoiceService.analyze(supplierId, file).subscribe({
      next: analysis => this.startReview(analysis),
      error: error => {
        this.analyzing.set(false);
        this.error.set(errorMessage(error));
      },
    });
  }

  async confirm() {
    const analysis = this.analysis();
    const file = this.file();
    if (!analysis || !file) return;

    this.confirming.set(true);
    this.error.set('');

    // El archivo se vuelve a enviar para guardarlo en el historial junto a lo registrado.
    let fileContent: string;
    try {
      fileContent = await readAsBase64(file);
    } catch {
      this.confirming.set(false);
      this.error.set('No se pudo leer el archivo de la factura. Vuelve a elegirlo.');
      return;
    }

    this.send(this.toRequest(analysis, file.name, fileContent));
  }

  /**
   * Envía la confirmación. Si la factura ya estaba registrada, la API la rechaza sin guardar nada
   * y se pregunta al usuario: en la demo se reutilizan los mismos archivos, así que puede querer
   * registrarla otra vez (su stock se suma al ya registrado).
   */
  private send(request: ConfirmInvoiceRequest) {
    this.invoiceService.confirm(request).subscribe({
      next: confirmed => {
        this.notice.set(
          `Factura ${confirmed.lotNumber} registrada: ${confirmed.unitsReceived} unidades de ` +
          `${confirmed.productsReceived} productos. La próxima factura de este proveedor reconocerá ` +
          `${confirmed.aliasesSaved} descripciones.`);
        this.startOver();
      },
      error: error => {
        const wantsToRegisterAgain = isAlreadyRegistered(error)
          && confirm('Esta factura ya fue registrada. ¿Seguro que quieres volverla a registrar?');
        if (wantsToRegisterAgain) {
          this.send({ ...request, registerAgain: true });
          return;
        }

        this.confirming.set(false);
        this.error.set(errorMessage(error));
      },
    });
  }

  // El usuario confirma que la factura va al proveedor que eligió, aunque el emisor diga otra cosa.
  keepChosenSupplier() {
    this.issuerDecided.set(true);
  }

  /**
   * Vuelve a analizar el mismo archivo con otro proveedor. Se analiza de nuevo, y no solo se cambia
   * el id, porque las líneas se concilian con las equivalencias de cada proveedor.
   */
  analyzeAs(supplierId: number) {
    this.analysis.set(null);
    this.lines.set([]);
    this.supplierId.set(supplierId);
    this.analyze();
  }

  // Vuelve al paso 1 conservando el proveedor, que suele repetirse en la siguiente factura.
  startOver() {
    this.analysis.set(null);
    this.lines.set([]);
    this.invoiceNumber.set('');
    this.setFile(null);
    this.showOriginal.set(false);
    this.confirming.set(false);
  }

  lineTotal(line: ReviewLine) {
    return Number(line.quantity) * Number(line.unitPrice);
  }

  // Convierte la respuesta del análisis en líneas editables, con el producto propuesto ya elegido.
  private startReview(analysis: InvoiceAnalysis) {
    const lines = analysis.lines.map(line => {
      const status = line.match.status;
      const proposesProduct = preselectedStatuses.includes(status) && line.match.candidates.length > 0;

      return {
        lineNumber: line.lineNumber,
        description: line.extracted.description,
        supplierItemCode: line.extracted.supplierItemCode,
        status,
        matchedBy: line.match.matchedBy,
        candidates: line.match.candidates,
        include: true,
        product: proposesProduct ? line.match.candidates[0] : null,
        quantity: line.extracted.quantity,
        unitPrice: line.extracted.unitPrice,
      };
    });

    // Sin emisor legible no hay nada que contrastar; en los demás casos decide el usuario.
    const issuerNeedsDecision = analysis.issuerCheck === 'OtherSupplier' || analysis.issuerCheck === 'NotRegistered';

    this.analysis.set(analysis);
    this.issuerDecided.set(!issuerNeedsDecision);
    this.invoiceNumber.set(analysis.invoiceNumber ?? '');
    this.lines.set(lines);
    this.analyzing.set(false);
  }

  // Solo las líneas incluidas; cantidades y precios como números (los inputs pueden dar texto).
  private toRequest(analysis: InvoiceAnalysis, fileName: string, fileContent: string): ConfirmInvoiceRequest {
    return {
      supplierId: analysis.supplierId,
      invoiceNumber: this.invoiceNumber().trim(),
      issueDate: analysis.issueDate,
      invoiceTotal: analysis.total,
      file: { fileName, content: fileContent },
      lines: this.includedLines.map(line => ({
        productId: line.product!.productId,
        originalDescription: line.description,
        supplierItemCode: line.supplierItemCode,
        quantity: Number(line.quantity),
        unitPrice: Number(line.unitPrice),
        productCreated: line.product!.created === true,
      })),
      registerAgain: false,
    };
  }
}

// La API respondió que la factura ya está registrada para el proveedor (código en ProblemDetails).
function isAlreadyRegistered(error: unknown) {
  return error instanceof HttpErrorResponse && error.error?.code === alreadyRegisteredCode;
}

/**
 * Contenido del archivo en base64, como lo espera la API dentro del JSON. Se lee como data URL
 * ("data:<tipo>;base64,<contenido>") y se quita el prefijo, que no es parte del archivo.
 */
function readAsBase64(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => {
      const dataUrl = reader.result as string;
      resolve(dataUrl.slice(dataUrl.indexOf(',') + 1));
    };
    reader.onerror = () => reject(reader.error);
    reader.readAsDataURL(file);
  });
}
