import { AfterViewInit, Component, Input } from '@angular/core';
import { OcadVersionerProvider } from '../../ocad-versioner.provider';
import { OcadReaderService } from '../../services/ocad-reader-service/ocad-reader-service';
import { Position } from 'geojson';

@Component({
  selector: 'ocad-map-viewer',
  templateUrl: './ocad-map-viewer.component.html',
  styleUrl: './ocad-map-viewer.component.scss',
})
export class OcadMapViewerComponent implements AfterViewInit {
  private leaflet: any | null = null;
  @Input({ required: true })
  public bboxOfMap: Position[] = [[]];
  private useOsmLayer: boolean = false;
  constructor(
    private provider: OcadVersionerProvider,
    private ocadReader: OcadReaderService
  ) {}

  ngAfterViewInit(): void {
    // Need to do it with this dynamic import, because Leaflet access the 'window' property, which is not defined when building the Angular app or in a server environment.
    // It seems like 'window' is referenced by Leaflet as soon as you import anything from Leaflet node_module.
    import('leaflet').then(async (leaflet) => {
      this.leaflet = leaflet;
      await this.initMap();
    });
  }

  private async initMap(): Promise<void> {
    const map = this.leaflet.map('map').setView(this.bboxOfMap[0], 13);
    if (this.useOsmLayer)
      this.leaflet
        .tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
          maxZoom: 19,
          attribution:
            '&copy; <a href="http://www.openstreetmap.org/copyright">OpenStreetMap</a>',
        })
        .addTo(map);

    const currentOcdFile = this.provider.getOcdFileHandle('current');
    const svgElement = await this.ocadReader.getSvgFromOcadObject(
      await currentOcdFile.getFile()
    );
    this.leaflet
      .svgOverlay(svgElement, [this.bboxOfMap[0], this.bboxOfMap[1]], {
        opacity: 2.0,
      })
      .addTo(map);
  }
}
