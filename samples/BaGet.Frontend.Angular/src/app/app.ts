import { Component, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Flash } from './api/screens';
import { SessionService } from './session/session.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './app.html',
})
export class App {
  protected readonly session = inject(SessionService);
  protected readonly flash = inject(Flash);
  private readonly router = inject(Router);

  protected signOut(everywhere: boolean): void {
    this.session.signOut(everywhere).subscribe(() => void this.router.navigate(['/sign-in']));
  }
}
