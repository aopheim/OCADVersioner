import { NgModule } from '@angular/core';
import { OcadFileUploaderComponent } from './ocad-file-uploader.component';
import { OcadVersionerComponent } from '../../ocad-versioner.component';
import { JsonDiffService } from '../../services/json-diff-service';
import { OcadMapViewerComponent } from '../ocad-map-viewer/ocad-map-viewer.component';
import { LeafletModule } from '@asymmetrik/ngx-leaflet';

@NgModule({
  imports: [
    OcadVersionerComponent,
    OcadFileUploaderComponent,
    OcadMapViewerComponent,
    LeafletModule,
  ],
  exports: [
    OcadVersionerComponent,
    OcadFileUploaderComponent,
    OcadMapViewerComponent,
  ],
  providers: [JsonDiffService],
})
export class OcadVersionerModule {}
