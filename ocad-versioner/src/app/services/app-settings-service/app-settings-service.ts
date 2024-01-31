import { Injectable } from '@angular/core';
import { AppSettings } from './app-settings.models';
import { isNil } from 'lodash-es';

@Injectable()
export class AppSettingsService {
  public get appSettings(): AppSettings {
    const json = localStorage.getItem('appSettings');
    if (isNil(json)) return {};
    return JSON.parse(json);
  }

  public set appSettings(val: AppSettings) {
    localStorage.setItem('appSettings', JSON.stringify(val));
  }
}
