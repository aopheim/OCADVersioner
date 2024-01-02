import { Component, ErrorHandler } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterOutlet } from '@angular/router';
import { OcadVersionerModule } from './ocad-versioner.module';
import { TranslateService } from '@ngx-translate/core';
import { LoggingService } from './services/logging/logging.service';
import { ErrorHandlerService } from './services/error-handler/error-handler.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, RouterOutlet, OcadVersionerModule],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss',
  providers: [
    { provide: ErrorHandler, useClass: ErrorHandlerService },
    LoggingService,
  ],
})
export class AppComponent {
  title = 'OCADVersioner';
  constructor(translate: TranslateService) {
    // this language will be used as a fallback when a translation isn't found in the current language
    translate.setDefaultLang('no');

    // the lang to use, if the lang isn't available, it will use the current loader to get them
    translate.use('no');
  }
}
