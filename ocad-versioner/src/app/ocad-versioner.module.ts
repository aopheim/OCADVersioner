import { NgModule } from '@angular/core';
import { OcadFileUploaderComponent } from './components/ocad-file-uploader/ocad-file-uploader.component';
import { OcadVersionerComponent } from './ocad-versioner.component';
import { JsonDiffService } from './services/json-diff-service';
import { OcadMapViewerComponent } from './components/ocad-map-viewer/ocad-map-viewer.component';
import { LeafletModule } from '@asymmetrik/ngx-leaflet';
import { OcadDiffTableComponent } from './components/ocad-diff-table/ocad-diff-table.component';

@NgModule({
  imports: [
    OcadVersionerComponent,
    OcadFileUploaderComponent,
    OcadMapViewerComponent,
    OcadDiffTableComponent,
    LeafletModule,
  ],
  exports: [
    OcadVersionerComponent,
    OcadFileUploaderComponent,
    OcadMapViewerComponent,
    OcadDiffTableComponent,
  ],
  providers: [JsonDiffService],
})
export class OcadVersionerModule {}
