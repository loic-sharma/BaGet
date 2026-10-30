import { Component, input } from '@angular/core';
import { AbstractControl } from '@angular/forms';

/** The messages of one control: the server's (`ScreenResult.errors`, see `applyServerErrors`) and the client-side `required`. */
@Component({
  selector: 'app-field-errors',
  template: `
    @if (control().touched || control().hasError('server')) {
      @if (control().hasError('required')) {
        <small class="error">Required.</small>
      }
      @for (message of serverMessages(); track message) {
        <small class="error">{{ message }}</small>
      }
    }
  `,
})
export class FieldErrors {
  readonly control = input.required<AbstractControl>();

  protected serverMessages(): string[] {
    return (this.control().getError('server') as string[] | null) ?? [];
  }
}
