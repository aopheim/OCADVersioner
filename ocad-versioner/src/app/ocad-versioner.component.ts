import { Component, OnInit } from '@angular/core';
import { FeatureCollection } from 'geojson';
import { BehaviorSubject, Observable, combineLatest, filter, map } from 'rxjs';
import { JsonDiffService } from './services/json-diff-service/json-diff-service';
import { OcadDiffDto } from './components/ocad-diff-table/ocad-diff-table/ocad-diff-table.models';
import { OcadDiffTableView } from './components/ocad-diff-table/ocad-diff-table/ocad-diff-table.component';
import { OcadVersionerProvider } from './ocad-versioner.provider';
import { OcadReaderService } from './services/ocad-reader-service/ocad-reader-service';
import { OcadDirectoryHelper } from './components/project-directory-selector/project-directory-selector.helper';

@Component({
  selector: 'ocad-versioner',
  templateUrl: './ocad-versioner.component.html',
  styleUrl: './ocad-versioner.component.scss',
})
export class OcadVersionerComponent implements OnInit {
  public newestVersion$: BehaviorSubject<FeatureCollection | null> =
    new BehaviorSubject<FeatureCollection | null>(null);
  public oldestVersion$: BehaviorSubject<FeatureCollection | null> =
    new BehaviorSubject<FeatureCollection | null>(null);
  public diffTable$: Observable<OcadDiffDto> = new Observable();
  public selectedTableView$: BehaviorSubject<OcadDiffTableView> =
    new BehaviorSubject<OcadDiffTableView>(OcadDiffTableView.Added);

  constructor(
    private jsonDiffService: JsonDiffService,
    public provider: OcadVersionerProvider,
    private ocadReader: OcadReaderService
  ) {}

  public OcadDiffTableView = OcadDiffTableView;
  ngOnInit(): void {
    this.diffTable$ = combineLatest([
      this.newestVersion$,
      this.oldestVersion$,
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
    if (isOriginal) this.newestVersion$.next(geoJson);
    else this.oldestVersion$.next(geoJson);
  }

  public setSelectedTableView(selectedView: OcadDiffTableView): void {
    this.selectedTableView$.next(selectedView);
  }

  public async setSelectedVersion(selectedVersionNumber: number) {
    const selectedOcdFile = this.provider.getOcdFileHandle(
      OcadDirectoryHelper.getVersionNameFromVersionNumber(selectedVersionNumber)
    );
    const newestFeatureCollection = await this.ocadReader.getGeoJsonFromOcdFile(
      await selectedOcdFile.getFile()
    );

    const versionNumberToCompare: number | null =
      this.getVersionNumberToCompare(selectedVersionNumber);

    const ocdFileToCompare = versionNumberToCompare
      ? this.provider.getOcdFileHandle(
          OcadDirectoryHelper.getVersionNameFromVersionNumber(
            versionNumberToCompare
          )
        )
      : null;

    if (!ocdFileToCompare) {
      this.oldestVersion$.next({ features: [], type: 'FeatureCollection' });
      this.newestVersion$.next(newestFeatureCollection);
      return;
    }
    const oldestFeatureCollection = await this.ocadReader.getGeoJsonFromOcdFile(
      await ocdFileToCompare.getFile()
    );

    this.newestVersion$.next(newestFeatureCollection);
    this.oldestVersion$.next(oldestFeatureCollection);
  }

  private getVersionNumberToCompare(
    selectedVersionNumber: number
  ): number | null {
    if (selectedVersionNumber === 0)
      return this.provider.getHighestVersionNumber();
    if (selectedVersionNumber === 1) return null;
    return selectedVersionNumber - 1;
  }
}
