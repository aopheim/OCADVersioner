import { AfterViewInit, Component, Input } from '@angular/core';
import { OcadVersionerProvider } from '../../ocad-versioner.provider';
import { OcadReaderService } from '../../services/ocad-reader-service/ocad-reader-service';
import { Position } from 'geojson';
import { LatLng, LatLngBounds, Map, map, svgOverlay, tileLayer } from 'leaflet';
import 'leaflet.sync';
import { isEqual, isNil } from 'lodash-es';
import {
  BehaviorSubject,
  switchMap,
  map as rxjsMap,
  distinctUntilChanged,
} from 'rxjs';
import { OcadDirectoryHelper } from '../project-directory-selector/project-directory-selector.helper';
import { getCenter } from 'geolib';

@Component({
  selector: 'ocad-map-viewer',
  templateUrl: './ocad-map-viewer.component.html',
  styleUrl: './ocad-map-viewer.component.scss',
})
export class OcadMapViewerComponent implements AfterViewInit {
  private _selectedVersionNumbers$: BehaviorSubject<SelectedVersionNumberDto | null> =
    new BehaviorSubject<SelectedVersionNumberDto | null>(null);
  private _bboxOfMap$: BehaviorSubject<Position[]> = new BehaviorSubject<
    Position[]
  >([[]]);

  private _newestMap: Map | null = null;
  private _oldestMap: Map | null = null;

  @Input({ required: true })
  public set bboxOfMap(val: Position[]) {
    this._bboxOfMap$.next(val);
  }
  public get bboxOfMap(): Position[] {
    return this._bboxOfMap$.value;
  }

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
    this._bboxOfMap$
      .pipe(
        distinctUntilChanged(this.bboxComparer),
        rxjsMap((coords) => {
          if (this._newestMap && coords.length > 0)
            this.setViewOfMap(this._newestMap, coords);
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
    if (this.useOsmLayer) {
      tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
        maxZoom: 19,
        attribution:
          '&copy; <a href="http://www.openstreetmap.org/copyright">OpenStreetMap</a>',
      }).addTo(myMap);
    }
    const svgElement = !isNil(versionNumber)
      ? await this.getSvgElementOfMap(
          OcadDirectoryHelper.getVersionNameFromVersionNumber(versionNumber)
        )
      : null;

    myMap.eachLayer((layer) => {
      // The OpenStreetMap layer has an attribution set. We do not want to remove that. If attribution is set on other layers, find another identifier
      if (!layer.options.attribution) myMap.removeLayer(layer);
    });
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

  private initializeMap(mapId: string, isNewest: boolean): Map {
    if (isNewest) {
      this._newestMap = map(mapId);
      this._newestMap = this.setViewOfMap(this._newestMap, this.bboxOfMap);
      return this._newestMap;
    } else {
      this._oldestMap = map(mapId);
      this._oldestMap = this.setViewOfMap(this._oldestMap, this.bboxOfMap);
      return this._oldestMap;
    }
  }

  private setViewOfMap(map: Map, bbox: Position[]): Map {
    if (bbox && bbox.length > 0 && bbox[0].length > 0) {
      const southWest = { lat: bbox[0][0], lon: bbox[0][1] };
      const northEast = { lat: bbox[1][0], lon: bbox[1][1] };
      let center: { longitude: number; latitude: number } | false = getCenter([
        southWest,
        northEast,
      ]);
      if (center === false)
        center = {
          latitude: bbox[0][0],
          longitude: bbox[0][1],
        };
      const bounds: LatLngBounds = new LatLngBounds(
        { lat: southWest.lat, lng: southWest.lon },
        { lat: northEast.lat, lng: northEast.lon }
      );
      map.fitBounds(bounds);
    }
    return map;
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
  private bboxComparer(previous: Position[], current: Position[]): boolean {
    if (isNil(current)) return true;
    return isEqual(previous, current);
  }
}

export interface SelectedVersionNumberDto {
  newestVersionNumber: number;
  oldestVersionNumber: number | null;
}
