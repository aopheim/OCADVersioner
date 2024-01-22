import { AfterViewInit, Component, Input } from '@angular/core';
import { OcadVersionerProvider } from '../../ocad-versioner.provider';
import { OcadReaderService } from '../../services/ocad-reader-service/ocad-reader-service';
import { Position } from 'geojson';
import { LatLng, LatLngBounds, Map, map, svgOverlay, tileLayer } from 'leaflet';
import 'leaflet.sync';
import { isNil } from 'lodash-es';
import { BehaviorSubject, switchMap } from 'rxjs';
import { OcadDirectoryHelper } from '../project-directory-selector/project-directory-selector.helper';

@Component({
  selector: 'ocad-map-viewer',
  templateUrl: './ocad-map-viewer.component.html',
  styleUrl: './ocad-map-viewer.component.scss',
})
export class OcadMapViewerComponent implements AfterViewInit {
  private _selectedVersionNumbers$: BehaviorSubject<SelectedVersionNumberDto | null> =
    new BehaviorSubject<SelectedVersionNumberDto | null>(null);

  private _newestMap: Map | null = null;
  private _oldestMap: Map | null = null;

  @Input({ required: true })
  public bboxOfMap: Position[] = [[]];

  @Input({ required: true })
  public set selectedVersionNumbers(val: SelectedVersionNumberDto | null) {
    this._selectedVersionNumbers$.next(val);
  }
  public get selectedVersionNumbers(): SelectedVersionNumberDto | null {
    return this._selectedVersionNumbers$.value;
  }
  private useOsmLayer: boolean = false;
  constructor(
    private provider: OcadVersionerProvider,
    private ocadReader: OcadReaderService
  ) {}

  async ngAfterViewInit(): Promise<void> {
    this._selectedVersionNumbers$
      .pipe(
        switchMap(async (selected) => {
          if (!selected) return;
          await this.setMaps(
            selected.newestVersionNumber,
            selected.oldestVersionNumber
          );
        })
      )
      .subscribe();
  }

  private async setMaps(
    newestVersion: number,
    oldestVersion: number | null
  ): Promise<void> {
    const newestMap = await this.loadMap(newestVersion, 'newestMap', true);
    const oldestMap = await this.loadMap(oldestVersion, 'oldestMap', false);
    newestMap.sync(oldestMap);
    oldestMap.sync(newestMap);
  }

  private async loadMap(
    versionNumber: number | null,
    mapId: string,
    isNewest: boolean
  ): Promise<Map> {
    const myMap = isNewest
      ? this._newestMap
        ? this._newestMap
        : this.initializeMap(mapId, isNewest)
      : this._oldestMap
      ? this._oldestMap
      : this.initializeMap(mapId, isNewest);
    if (this.useOsmLayer)
      tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
        maxZoom: 19,
        attribution:
          '&copy; <a href="http://www.openstreetmap.org/copyright">OpenStreetMap</a>',
      }).addTo(myMap);
    const svgElement = !isNil(versionNumber)
      ? await this.getSvgElementOfMap(
          OcadDirectoryHelper.getVersionNameFromVersionNumber(versionNumber)
        )
      : null;

    myMap.eachLayer((layer) => myMap.removeLayer(layer));
    if (svgElement) {
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
    return myMap;
  }

  initializeMap(mapId: string, isNewest: boolean): Map {
    if (isNewest) {
      this._newestMap = map(mapId);
      this._newestMap.setView(
        { lat: this.bboxOfMap[0][0], lng: this.bboxOfMap[0][1] },
        13
      );
      return this._newestMap;
    } else {
      this._oldestMap = map(mapId);
      this._oldestMap.setView(
        { lat: this.bboxOfMap[0][0], lng: this.bboxOfMap[0][1] },
        13
      );
      return this._oldestMap;
    }
  }

  private async getSvgElementOfMap(
    versionName: string
  ): Promise<SVGElement | null> {
    const ocdFile = this.provider.getOcdFileHandle(versionName);
    if (isNil(ocdFile)) return null;
    const svgElement = await this.ocadReader.getSvgFromOcadObject(
      await ocdFile.getFile()
    );
    return svgElement;
  }
}

export interface SelectedVersionNumberDto {
  newestVersionNumber: number;
  oldestVersionNumber: number | null;
}
