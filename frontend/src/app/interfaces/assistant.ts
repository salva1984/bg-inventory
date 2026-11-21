export type AssistantRole = 'User' | 'Assistant';

export interface AssistantMessage {
  role: AssistantRole;
  content: string;
}

export interface AssistantReply {
  content: string;
}
