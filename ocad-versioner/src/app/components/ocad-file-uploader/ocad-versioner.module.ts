import { NgModule } from '@angular/core';
import { OcadFileUploaderComponent } from './ocad-file-uploader.component';
import { OcadVersionerComponent } from '../../ocad-versioner.component';

@NgModule({
  imports: [OcadVersionerComponent, OcadFileUploaderComponent],
  exports: [OcadVersionerComponent, OcadFileUploaderComponent],
})
export class OcadVersionerModule {}
