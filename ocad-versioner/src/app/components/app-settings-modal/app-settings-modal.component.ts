import {
  AfterViewInit,
  Component,
  ElementRef,
  OnInit,
  ViewChild,
} from '@angular/core';
import {
  AbstractControl,
  FormControl,
  FormGroup,
  Validators,
} from '@angular/forms';
import { AppSettingsService } from '../../services/app-settings-service/app-settings-service';
import fullEpsgIndex from 'epsg-index/all.json';
import { BehaviorSubject, Observable, filter, map, of } from 'rxjs';
import { isNil } from 'lodash-es';
// import { TranslateService } from '@ngx-translate/core';
// import Popover from 'bootstrap/js/dist/popover';

@Component({
  selector: 'app-settings-modal',
  templateUrl: './app-settings-modal.component.html',
  styleUrl: './app-settings-modal.component.scss',
})
export class AppSettingsModalComponent implements OnInit, AfterViewInit {
  private static AcceptedLanguageCodes: string[] = ['en', 'no'];
  @ViewChild('openModalButton') openModalButton: ElementRef | null = null;

  private _epsgIndex: EpsgIndex;
  public epsgSuggestions$: Observable<EpsgIndexItem[]>;
  public form: FormGroup<AppSettingsForm>;
  public selectedEpsgIndexItem$: BehaviorSubject<EpsgIndexItem | null> =
    new BehaviorSubject<EpsgIndexItem | null>(null);
  public selectedLanguageCode$: BehaviorSubject<string> =
    new BehaviorSubject<string>('');

  constructor(
    private appSettingsService: AppSettingsService // private translate: TranslateService
  ) {
    const appSettings = appSettingsService.appSettings$.value;
    this.form = new FormGroup<AppSettingsForm>({
      georeferencing: new FormGroup<GeoReferenceSettingsForm>({
        epsgNumber: new FormControl(
          appSettings?.georeferencing?.epsgNumber ?? null,
          [Validators.min(1)]
        ),
      }),
      languageSelection: new FormGroup<LanguageSelectionForm>({
        selectedLanguageCode: new FormControl(
          appSettings?.languageSelection?.selectedLanguageCode ?? 'en',
          { validators: [this.languageCodeValidator] }
        ),
      }),
    });
    this._epsgIndex = fullEpsgIndex as EpsgIndex;
    this.epsgSuggestions$ = of([]);
    this.selectedLanguageCode$.next(
      appSettings?.languageSelection?.selectedLanguageCode ??
        AppSettingsService.DefaultLanguageCode
    );
    if (appSettings?.georeferencing?.epsgNumber)
      this.onEpsgItemSelected(appSettings.georeferencing.epsgNumber);
  }
  ngAfterViewInit(): void {
    // TODO: This currently breaks the dropdown component in directory-selector. It is something with the import ordering of bootstrap which causes it.
    // import Popover from 'bootstrap' breaks it.
    // https://stackoverflow.com/questions/51173263/import-static-json-file-from-assets-dir-in-component-angular
    // https://discourse.aurelia.io/t/bootstrap-import-bootstrap-breaks-dropdown-menu-in-navbar/641
    // this.translate
    //   .get('#_georeferencingExplanation')
    //   .subscribe((translation) => {
    //     const popoverTriggerList = document.querySelectorAll(
    //       '[data-bs-toggle="popover"]'
    //     );
    //     popoverTriggerList.forEach(
    //       (popoverTriggerEl) =>
    //         new Popover(popoverTriggerEl, { content: translation })
    //     );
    //   });
  }

  ngOnInit(): void {
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

  public onLanguageSelected(languageCode: string) {
    this.form.controls.languageSelection?.controls.selectedLanguageCode?.setValue(
      languageCode
    );
    this.selectedLanguageCode$.next(languageCode);
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
    this.appSettingsService.setAppSetting(this.form.value);
  }

  private languageCodeValidator(
    control: AbstractControl
  ): { [key: string]: boolean } | null {
    const isValid = AppSettingsModalComponent.AcceptedLanguageCodes.includes(
      control.value
    );
    if (isValid) return null;
    return { '#_notAcceptedLanguage': true };
  }
}

interface AppSettingsForm {
  georeferencing?: FormGroup<GeoReferenceSettingsForm>;
  languageSelection?: FormGroup<LanguageSelectionForm>;
}

interface GeoReferenceSettingsForm {
  epsgNumber?: FormControl<number | null>;
}

interface LanguageSelectionForm {
  selectedLanguageCode?: FormControl<string | null>;
}

export interface EpsgIndexItem {
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

export interface EpsgIndex {
  [index: string]: EpsgIndexItem;
}
