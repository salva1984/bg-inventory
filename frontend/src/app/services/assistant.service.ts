import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../environments/environment';
import { AssistantMessage, AssistantReply } from '../interfaces/assistant';

@Injectable({ providedIn: 'root' })
export class AssistantService {
  private http = inject(HttpClient);
  private url = `${environment.apiUrl}/assistant/chat`;

  ask(messages: AssistantMessage[]) {
    return this.http.post<AssistantReply>(this.url, { messages });
  }
}
