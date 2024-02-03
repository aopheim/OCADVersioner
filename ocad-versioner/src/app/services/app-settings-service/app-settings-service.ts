import { Injectable } from '@angular/core';
import { AppSettings } from './app-settings.models';
import { isNil } from 'lodash-es';
import { BehaviorSubject } from 'rxjs';

@Injectable()
export class AppSettingsService {
  public appSettings$: BehaviorSubject<AppSettings> =
    new BehaviorSubject<AppSettings>({});

  public setAppSetting(val: AppSettings) {
    localStorage.setItem('appSettings', JSON.stringify(val));
    this.appSettings$.next(val);
  }

  constructor() {
    this.appSettings$.next(this.readAppSettingsAsJson());
  }

  private readAppSettingsAsJson(): AppSettings {
    const json = localStorage.getItem('appSettings');
    if (isNil(json)) return {};
    return JSON.parse(json);
  }
}
