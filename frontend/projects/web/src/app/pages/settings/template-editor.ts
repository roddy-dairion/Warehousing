import { Component, computed, inject, OnDestroy, OnInit, signal } from '@angular/core';
import { DatePipe, formatDate } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { ConfirmationService } from 'primeng/api';
import { Button } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { Select } from 'primeng/select';
import { Tabs, TabList, Tab } from 'primeng/tabs';
import { Skeleton } from 'primeng/skeleton';
import { ApiError, DocumentType, SettingsApi, TemplateDto } from '@wh/shared';
import { ApiFeedback } from '../../ui/api-feedback';
import { LoadError } from '../../ui/load-error';
import { readBlobError } from '../../ui/blob-error';

/** Edit the HTML (Liquid) layouts of the packing slip and the delivery note, with a PDF preview and version history. */
@Component({
  selector: 'wh-template-editor',
  imports: [DatePipe, FormsModule, Button, InputText, Select, Tabs, TabList, Tab, Skeleton, LoadError],
  templateUrl: './template-editor.html',
  styleUrl: './template-editor.scss',
})
export class TemplateEditor implements OnInit, OnDestroy {
  private readonly api = inject(SettingsApi);
  private readonly feedback = inject(ApiFeedback);
  private readonly confirm = inject(ConfirmationService);
  private readonly sanitizer = inject(DomSanitizer);

  protected readonly type = signal<DocumentType>('deliveryNote');
  protected readonly template = signal<TemplateDto | null>(null);
  /** The version being looked at (null = current). */
  protected readonly viewing = signal<number | null>(null);
  protected readonly loadError = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly previewUrl = signal<SafeResourceUrl | null>(null);
  protected readonly previewError = signal<string | null>(null);
  private rawPreviewUrl: string | null = null;

  protected html = '';
  protected comment = '';
  protected readonly dirty = signal(false);
  protected readonly isCurrent = computed(() => {
    const t = this.template();
    return !!t && t.versions.find((v) => v.version === t.version)?.isCurrent === true;
  });
  protected readonly versionOptions = computed(() => (this.template()?.versions ?? []).map((v) => ({
    value: v.version,
    label: `v${v.version}${v.isCurrent ? ' (current)' : ''} · ${formatDate(v.createdAt, 'dd.MM.yyyy HH:mm', 'en-US')}${v.comment ? ' · ' + v.comment : ''}`,
  })));

  ngOnInit(): void {
    void this.load();
  }

  ngOnDestroy(): void {
    if (this.rawPreviewUrl) URL.revokeObjectURL(this.rawPreviewUrl);
  }

  protected selectType(value: string | number | undefined): void {
    if (this.dirty() && !window.confirm('Discard your unsaved changes?')) return;
    this.type.set(value as DocumentType);
    this.viewing.set(null);
    void this.load();
  }

  protected selectVersion(version: number): void {
    if (this.dirty() && !window.confirm('Discard your unsaved changes?')) return;
    this.viewing.set(version);
    void this.load();
  }

  protected async load(): Promise<void> {
    this.loadError.set(null);
    try {
      const t = await this.api.template(this.type(), this.viewing() ?? undefined);
      this.template.set(t);
      this.html = t.html;
      this.comment = '';
      this.dirty.set(false);
      await this.preview();
    } catch (e) {
      this.loadError.set(ApiError.from(e).userMessage);
    }
  }

  protected onEdit(): void {
    this.dirty.set(true);
  }

  protected async preview(): Promise<void> {
    this.busy.set(true);
    this.previewError.set(null);
    try {
      const blob = await this.api.preview(this.type(), this.html);
      if (this.rawPreviewUrl) URL.revokeObjectURL(this.rawPreviewUrl);
      this.rawPreviewUrl = URL.createObjectURL(blob);
      this.previewUrl.set(this.sanitizer.bypassSecurityTrustResourceUrl(this.rawPreviewUrl));
    } catch (e) {
      this.previewError.set((await readBlobError(e)).userMessage);
    } finally {
      this.busy.set(false);
    }
  }

  protected async save(): Promise<void> {
    this.busy.set(true);
    try {
      const t = await this.api.saveTemplate(this.type(), this.html, this.comment.trim() || null);
      this.template.set(t);
      this.viewing.set(null);
      this.dirty.set(false);
      this.comment = '';
      this.feedback.success(`Saved as version ${t.version}. New documents use it from now on.`);
    } catch (e) {
      this.feedback.error(e);
    } finally {
      this.busy.set(false);
    }
  }

  protected restore(): void {
    const t = this.template();
    if (!t) return;
    this.confirm.confirm({
      header: 'Restore version',
      message: `Make version ${t.version} the current template again? It is saved as a new version.`,
      acceptLabel: 'Restore',
      rejectLabel: 'Back',
      accept: async () => {
        try {
          const restored = await this.api.restoreTemplate(this.type(), t.version);
          this.viewing.set(null);
          this.template.set(restored);
          this.html = restored.html;
          this.dirty.set(false);
          this.feedback.success(`Version ${t.version} restored as version ${restored.version}.`);
        } catch (e) {
          this.feedback.error(e);
        }
      },
    });
  }

  protected insert(textarea: HTMLTextAreaElement, placeholder: string): void {
    const token = `{{ ${placeholder} }}`;
    const start = textarea.selectionStart ?? this.html.length;
    const end = textarea.selectionEnd ?? start;
    this.html = this.html.slice(0, start) + token + this.html.slice(end);
    this.dirty.set(true);
    setTimeout(() => {
      textarea.focus();
      textarea.setSelectionRange(start + token.length, start + token.length);
    });
  }

  /** Clickable placeholders from the API's descriptions; line fields are offered as `line.x` for use inside the lines loop. */
  protected placeholderGroups(): { title: string; items: string[] }[] {
    return (this.template()?.placeholders ?? []).map((text) => {
      if (text.startsWith('lines[]:')) {
        const fields = text.slice('lines[]:'.length).replace(/\([^)]*\)/g, '').split(',').map((s) => s.trim().replace('[]', '')).filter(Boolean);
        return { title: 'line', items: fields.map((f) => `line.${f}`) };
      }
      const items = Array.from(new Set(text.replace(/\([^)]*\)/g, '').match(/\b[a-z_]+\.[a-z_]+\b/g) ?? []));
      return { title: items[0]?.split('.')[0] ?? '', items };
    }).filter((g) => g.items.length > 0);
  }
}
