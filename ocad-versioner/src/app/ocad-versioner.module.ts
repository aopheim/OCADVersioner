import { NgModule } from '@angular/core';
import { OcadVersionerComponent } from './ocad-versioner.component';
import { JsonDiffService } from './services/json-diff-service';
import { OcadVersionerComponetsModule as OcadVersionerComponentsModule } from './components/ocad-versioner-components.module';
import { CommonModule } from '@angular/common';
import { TranslateLoader, TranslateModule } from '@ngx-translate/core';
import { HttpClient, HttpClientModule } from '@angular/common/http';
import { TranslateHttpLoader } from '@ngx-translate/http-loader';
import { OcadVersionerProvider } from './ocad-versioner.provider';
import { OcadFileSystemService } from './services/ocad-file-system-service';

// AoT requires an exported function for factories
export function HttpLoaderFactory(http: HttpClient) {
  return new TranslateHttpLoader(http);
}
@NgModule({
  imports: [
    OcadVersionerComponentsModule,
    CommonModule,
    HttpClientModule,
    TranslateModule.forRoot({
      loader: {
        provide: TranslateLoader,
        useFactory: HttpLoaderFactory,
        deps: [HttpClient],
      },
      defaultLanguage: 'no',
    }),
  ],
  declarations: [OcadVersionerComponent],
  exports: [OcadVersionerComponent],
  providers: [JsonDiffService, OcadVersionerProvider, OcadFileSystemService],
})
export class OcadVersionerModule {}
