import { Component, ErrorHandler } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterOutlet } from '@angular/router';
import { OcadVersionerModule } from './ocad-versioner.module';
import { TranslateService } from '@ngx-translate/core';
import { LoggingService } from './services/logging/logging.service';
import { ErrorHandlerService } from './services/error-handler/error-handler.service';
import { AppSettingsService } from './services/app-settings-service/app-settings-service';
import { distinctUntilChanged, map } from 'rxjs';
import { AppSettings } from './services/app-settings-service/app-settings.models';

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
  constructor(
    private translate: TranslateService,
    appSettingsService: AppSettingsService
  ) {
    this.translate.setDefaultLang(AppSettingsService.DefaultLanguageCode);
    appSettingsService.appSettings$
      .pipe(
        distinctUntilChanged(this.isLanguageSimilar),
        map((settings) =>
          this.translate.use(
            settings?.languageSelection?.selectedLanguageCode ??
              AppSettingsService.DefaultLanguageCode
          )
        )
      )
      .subscribe();
  }

  private isLanguageSimilar(prev: AppSettings, current: AppSettings): boolean {
    return (
      prev.languageSelection?.selectedLanguageCode ===
      current.languageSelection?.selectedLanguageCode
    );
  }
}
