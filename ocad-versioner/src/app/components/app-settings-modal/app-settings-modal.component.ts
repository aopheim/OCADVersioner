import { Component, ElementRef, OnInit, ViewChild } from '@angular/core';
import { FormControl, FormGroup, Validators } from '@angular/forms';
import { AppSettingsService } from '../../services/app-settings-service/app-settings-service';
import fullEpsgIndex from 'epsg-index/all.json';
import { BehaviorSubject, Observable, filter, map, of } from 'rxjs';
import { isNil } from 'lodash-es';
import { Popover } from 'bootstrap';
import { TranslateService } from '@ngx-translate/core';

@Component({
  selector: 'app-settings-modal',
  templateUrl: './app-settings-modal.component.html',
  styleUrl: './app-settings-modal.component.scss',
})
export class AppSettingsModalComponent implements OnInit {
  @ViewChild('openModalButton') openModalButton: ElementRef | null = null;

  private _epsgIndex: EpsgIndex;
  public epsgSuggestions$: Observable<EpsgIndexItem[]>;
  public form: FormGroup<AppSettingsForm>;
  public selectedEpsgIndexItem$: BehaviorSubject<EpsgIndexItem | null> =
    new BehaviorSubject<EpsgIndexItem | null>(null);

  constructor(
    private appSettingsService: AppSettingsService,
    private translate: TranslateService
  ) {
    const appSettings = appSettingsService.appSettings;
    this.form = new FormGroup<AppSettingsForm>({
      georeferencing: new FormGroup<GeoReferenceSettingsForm>({
        epsgNumber: new FormControl(
          appSettings?.georeferencing?.epsgNumber ?? null,
          [Validators.min(1024), Validators.max(32767)]
        ),
      }),
    });
    this._epsgIndex = fullEpsgIndex as EpsgIndex;
    this.epsgSuggestions$ = of([]);
    if (appSettings?.georeferencing?.epsgNumber)
      this.onEpsgItemSelected(appSettings.georeferencing.epsgNumber);
  }
  ngOnInit(): void {
    this.translate.get('#_georeferencingExplanation').subscribe((trans) => {
      const popoverTriggerList = document.querySelectorAll(
        '[data-bs-toggle="popover"]'
      );
      popoverTriggerList.forEach(
        (popoverTriggerEl) => new Popover(popoverTriggerEl, { content: trans })
      );
    });
    this.epsgSuggestions$ =
      this.form.controls.georeferencing?.controls.epsgNumber?.valueChanges.pipe(
        filter(
          (number) =>
            !isNil(number) && !isNaN(number) && !!number && number >= 0
        ),
        map((currentNumber) => {
          const currentNumberAsString = '' + currentNumber;
          const allIndexes = Object.keys(this._epsgIndex);
          const matchingIndexes = allIndexes.filter((i) =>
            i.includes(currentNumberAsString)
          );
          if (isNil(matchingIndexes) || matchingIndexes.length === 0) return [];
          const items: EpsgIndexItem[] = [];
          matchingIndexes.map((i) => items.push(this._epsgIndex[i]));
          return items.slice(0, 7);
        })
      ) ?? of([]);
  }

  public onEpsgItemSelected(epsgIndex: number) {
    this.form.controls.georeferencing?.controls.epsgNumber?.setValue(epsgIndex);
    this.form.controls.georeferencing?.controls.epsgNumber?.disable(
      // Need to set these for validation to not be invalidated
      {
        onlySelf: true,
        emitEvent: false,
      }
    );
    this.selectedEpsgIndexItem$.next(this._epsgIndex[epsgIndex]);
  }

  public onRemoveEpsg(): void {
    this.form.controls.georeferencing?.controls.epsgNumber?.setValue(null);
    this.form.controls.georeferencing?.controls.epsgNumber?.enable();
    this.selectedEpsgIndexItem$.next(null);
  }

  public onCancelClicked(): void {
    this.form.reset();
  }

  public onSaveClicked(): void {
    if (!this.form.valid) return;
    this.appSettingsService.appSettings = this.form.value;
  }
}

interface AppSettingsForm {
  georeferencing?: FormGroup<GeoReferenceSettingsForm>;
}

interface GeoReferenceSettingsForm {
  epsgNumber?: FormControl<number | null>;
}

interface EpsgIndexItem {
  code: string;
  kind: string;
  name: string;
  wkt: string | null;
  proj4: string | null;
  bbox: number[];
  unit: string | null;
  area: string | null;
  accuracy: number | null;
}

interface EpsgIndex {
  [index: string]: EpsgIndexItem;
}
