import { afterNextRender, Component, ElementRef, inject, Injector, OnDestroy, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { Subscription } from 'rxjs';
import { assistantMaxMessages, assistantMessageMaxLength } from '../../core/constants/validation';
import { AssistantMessage } from '../../interfaces/assistant';
import { AssistantService } from '../../services/assistant.service';
import { errorMessage } from '../../shared/utils/error-message';

@Component({
  selector: 'app-assistant',
  imports: [FormsModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  templateUrl: './assistant.html',
  styleUrl: './assistant.css',
})
export class Assistant implements OnDestroy {
  private assistantService = inject(AssistantService);
  private injector = inject(Injector);
  private log = viewChild<ElementRef<HTMLElement>>('log');

  readonly messageMaxLength = assistantMessageMaxLength;

  readonly examples = [
    '¿Qué productos debo reponer?',
    '¿Tenemos teclados en stock?',
    '¿Qué proveedor vende más barato el mouse?',
  ];

  messages = signal<AssistantMessage[]>([]);
  question = signal('');
  loading = signal(false);
  error = signal('');

  private request?: Subscription;

  ngOnDestroy() {
    this.request?.unsubscribe();
  }

  send(text: string) {
    const question = text.trim();
    if (!question || this.loading()) return;

    const previous = this.messages();
    const conversation: AssistantMessage[] = [...previous, { role: 'User', content: question }];
    this.messages.set(conversation);
    this.question.set('');
    this.loading.set(true);
    this.error.set('');
    this.scrollToEnd();

    this.request = this.assistantService.ask(this.historyToSend(conversation)).subscribe({
      next: reply => {
        this.messages.update(messages => [...messages, { role: 'Assistant', content: reply.content }]);
        this.loading.set(false);
        this.scrollToEnd();
      },
      error: error => {
        this.messages.set(previous);
        this.question.set(question);
        this.loading.set(false);
        this.error.set(errorMessage(error));
      },
    });
  }

  // Enter envía; Shift+Enter agrega un salto de línea.
  sendOnEnter(event: Event) {
    if ((event as KeyboardEvent).shiftKey) return;
    event.preventDefault();
    this.send(this.question());
  }

  // Cancela la pregunta en curso: su respuesta no debe aparecer en la conversación nueva.
  clear() {
    this.request?.unsubscribe();
    this.loading.set(false);
    this.messages.set([]);
    this.error.set('');
  }

  // Solo se envían los últimos mensajes y el recorte empieza en una pregunta: algunos modelos
  // rechazan una conversación que abre con una respuesta.
  private historyToSend(conversation: AssistantMessage[]) {
    const recent = conversation.slice(-assistantMaxMessages);
    const firstQuestion = recent.findIndex(message => message.role === 'User');
    return recent.slice(firstQuestion);
  }

  private scrollToEnd() {
    afterNextRender(() => {
      const log = this.log()?.nativeElement;
      if (log) log.scrollTop = log.scrollHeight;
    }, { injector: this.injector });
  }
}
