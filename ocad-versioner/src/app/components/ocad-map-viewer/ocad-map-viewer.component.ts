import { AfterViewInit, Component, Input } from '@angular/core';
import { OcadVersionerProvider } from '../../ocad-versioner.provider';
import { OcadReaderService } from '../../services/ocad-reader-service/ocad-reader-service';
import { Position } from 'geojson';
import { LatLng, LatLngBounds, map, svgOverlay, tileLayer } from 'leaflet';

@Component({
  selector: 'ocad-map-viewer',
  templateUrl: './ocad-map-viewer.component.html',
  styleUrl: './ocad-map-viewer.component.scss',
})
export class OcadMapViewerComponent implements AfterViewInit {
  @Input({ required: true })
  public bboxOfMap: Position[] = [[]];
  private useOsmLayer: boolean = false;
  constructor(
    private provider: OcadVersionerProvider,
    private ocadReader: OcadReaderService
  ) {}

  async ngAfterViewInit(): Promise<void> {
    await this.initMap();
  }

  private async initMap(): Promise<void> {
    const myMap = map('map').setView(
      { lat: this.bboxOfMap[0][0], lng: this.bboxOfMap[0][1] },
      13
    );
    if (this.useOsmLayer)
      tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
        maxZoom: 19,
        attribution:
          '&copy; <a href="http://www.openstreetmap.org/copyright">OpenStreetMap</a>',
      }).addTo(myMap);

    const currentOcdFile = this.provider.getOcdFileHandle('current');
    const svgElement = await this.ocadReader.getSvgFromOcadObject(
      await currentOcdFile.getFile()
    );
    svgOverlay(
      svgElement,
      new LatLngBounds(
        new LatLng(this.bboxOfMap[0][0], this.bboxOfMap[0][1]),
        new LatLng(this.bboxOfMap[1][0], this.bboxOfMap[1][1])
      ),
      {
        opacity: 1.0,
      }
    ).addTo(myMap);
  }
}
