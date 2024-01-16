import { AfterViewInit, Component, Input } from '@angular/core';
import { OcadVersionerProvider } from '../../ocad-versioner.provider';
import { OcadReaderService } from '../../services/ocad-reader-service/ocad-reader-service';
import { Position } from 'geojson';
import { LatLng, LatLngBounds,  Map, map, svgOverlay, tileLayer,  } from 'leaflet';
import "leaflet.sync"
import { isNil } from 'lodash-es';
import { BehaviorSubject,  map as rxjsMap } from 'rxjs';
import { OcadDirectoryHelper } from '../project-directory-selector/project-directory-selector.helper';

@Component({
  selector: 'ocad-map-viewer',
  templateUrl: './ocad-map-viewer.component.html',
  styleUrl: './ocad-map-viewer.component.scss',
})
export class OcadMapViewerComponent implements AfterViewInit {
  private _newestVersionNumber: number = 0
  private _loadNewestMap$: BehaviorSubject<boolean> = new BehaviorSubject<boolean>(false); 
  private _oldestVersionNumber: number | null = null;
  private _loadOldestMap$: BehaviorSubject<boolean> = new BehaviorSubject<boolean>(false); 
  
  @Input({ required: true })
  public bboxOfMap: Position[] = [[]];

  @Input({required: true})
  public set newestVersionNumber(val: number) {
    if(val === this._newestVersionNumber) return;
    this._newestVersionNumber = val;
    this._loadNewestMap$.next(true);
  }
  public get newestVersionNumber(): number{ 
    return this._newestVersionNumber;
  }
  
  @Input({required: true})
  public set oldestVersionNumber(val: number | null){
    if(val === this._oldestVersionNumber) return;
    this._oldestVersionNumber = val;
    this._loadOldestMap$.next(true);
  }
  public get oldestVersionNumber(): number | null {
    return this._oldestVersionNumber;
  }
  
  public newestMapLoading: boolean = false;
  public oldestMapLoading: boolean = false;
  private useOsmLayer: boolean = false;
  constructor(
    private provider: OcadVersionerProvider,
    private ocadReader: OcadReaderService,
  ) {}

  async ngAfterViewInit(): Promise<void> {
    await this.initMap();
    this._loadNewestMap$.pipe(rxjsMap(async () => {
      await this.loadNewestMap();
    })).subscribe();
    this._loadOldestMap$.pipe(rxjsMap(async () => {
      await this.loadOldestMap();
    })).subscribe();
  }

  private async loadNewestMap(): Promise<Map>{
    console.log('loading newest ', this.newestVersionNumber)
    const newestMap = map('newestMap').setView(
      { lat: this.bboxOfMap[0][0], lng: this.bboxOfMap[0][1] },
      13
    );
    if (this.useOsmLayer)
      tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
        maxZoom: 19,
        attribution:
          '&copy; <a href="http://www.openstreetmap.org/copyright">OpenStreetMap</a>',
      }).addTo(newestMap);
    const newestSvgElement = await this.getSvgElementOfMap(OcadDirectoryHelper.getVersionNameFromVersionNumber(this.newestVersionNumber));

    if(newestSvgElement)
      svgOverlay(
        newestSvgElement,
        new LatLngBounds(
          new LatLng(this.bboxOfMap[0][0], this.bboxOfMap[0][1]),
          new LatLng(this.bboxOfMap[1][0], this.bboxOfMap[1][1])
        ),
        {
          opacity: 1.0,
        }
      ).addTo(newestMap);
      return newestMap;
  }

  private async loadOldestMap(): Promise<Map> {
    console.log('loading oldest for ', this.oldestVersionNumber);
    const oldestMap = map('oldestMap').setView(
      { lat: this.bboxOfMap[0][0], lng: this.bboxOfMap[0][1] },
      13
    );
    const oldestSvgElement = this.oldestVersionNumber ? await this.getSvgElementOfMap(OcadDirectoryHelper.getVersionNameFromVersionNumber(this.oldestVersionNumber)) : null;
    if(oldestSvgElement)
      svgOverlay(
      oldestSvgElement,
      new LatLngBounds(
        new LatLng(this.bboxOfMap[0][0], this.bboxOfMap[0][1]),
        new LatLng(this.bboxOfMap[1][0], this.bboxOfMap[1][1])
      ),
      {
        opacity: 1.0,
      }
    ).addTo(oldestMap);
    return oldestMap;
  }

  private async initMap(): Promise<void> {
    const newestMap = await this.loadNewestMap();
    const oldestMap = await this.loadOldestMap();
    newestMap.sync(oldestMap);
    oldestMap.sync(newestMap);
    
  }
  private async  getSvgElementOfMap(versionName: string): Promise<SVGElement | null> {
    const ocdFile = this.provider.getOcdFileHandle(versionName);
    if(isNil(ocdFile)) return null;
    const svgElement = await this.ocadReader.getSvgFromOcadObject(
      await ocdFile.getFile()
    );
    return svgElement;
  }
}
