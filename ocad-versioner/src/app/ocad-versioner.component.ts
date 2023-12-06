import { Component, OnInit } from '@angular/core';
import { FeatureCollection } from 'geojson';
import { BehaviorSubject, Observable, combineLatest, filter, map } from 'rxjs';
import { JsonDiffService } from './services/json-diff-service';
import { OcadDiffDto } from './components/ocad-diff-table/ocad-diff-table/ocad-diff-table.models';
import { OcadDiffTableView } from './components/ocad-diff-table/ocad-diff-table/ocad-diff-table.component';
import { OcadVersionerProvider } from './ocad-versioner.provider';

@Component({
  selector: 'ocad-versioner',
  templateUrl: './ocad-versioner.component.html',
  styleUrl: './ocad-versioner.component.scss',
})
export class OcadVersionerComponent implements OnInit {
  public originalAsGeoJson$: BehaviorSubject<FeatureCollection | null> =
    new BehaviorSubject<FeatureCollection | null>(null);
  public versionedAsGeoJson$: BehaviorSubject<FeatureCollection | null> =
    new BehaviorSubject<FeatureCollection | null>(null);
  public diffTable$: Observable<OcadDiffDto> = new Observable();
  public selectedTableView$: BehaviorSubject<OcadDiffTableView> =
    new BehaviorSubject<OcadDiffTableView>(OcadDiffTableView.Added);

  constructor(
    private jsonDiffService: JsonDiffService,
    public provider: OcadVersionerProvider
  ) {}

  public OcadDiffTableView = OcadDiffTableView;
  ngOnInit(): void {
    this.diffTable$ = combineLatest([
      this.originalAsGeoJson$,
      this.versionedAsGeoJson$,
    ]).pipe(
      filter(([current, versioned]) => current !== null && versioned !== null),
      map(([current, versioned]) => {
        return this.jsonDiffService.getJsonDiff(versioned!, current!);
      })
    );
  }

  public handleLoadedGeoJsonFile(
    geoJson: FeatureCollection,
    isOriginal: boolean
  ) {
    if (isOriginal) this.originalAsGeoJson$.next(geoJson);
    else this.versionedAsGeoJson$.next(geoJson);
  }

  public setSelectedTableView(selectedView: OcadDiffTableView): void {
    this.selectedTableView$.next(selectedView);
  }
}
